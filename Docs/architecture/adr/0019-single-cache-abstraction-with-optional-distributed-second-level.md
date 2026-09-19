# ADR 0019: One caching abstraction with an optional distributed second level

**Status:** Accepted  
**Date:** 2026-09-04  
**Decision owners:** Workslip maintainers  
**Extends:** ADR 0001 (deployment-owned secret lifecycle) with one additional optional runtime secret

## Context

The API runs on Azure Container Apps. `src/BE/infrastructure/aca/app.bicep`
scales `ca-workslip-live-app` from zero to four replicas on an HTTP rule, so at
any moment above idle there is more than one process serving requests. Every
cache in the API is per-process.

**Reference data and job lists already cache through `HybridCache`.**
`ServiceConfiguration.cs` calls `AddHybridCache()`, and
`EfReferenceDataRepository`, `JobService` and `JobLifecycleService` use it. But
no `IDistributedCache` was registered anywhere in the solution, so `HybridCache`
ran with an L1 only. Each replica held its own copy, and
`RemoveByTagAsync("all")` from `CacheEndpoints.ClearCachesAsync` cleared the
memory of exactly one process — whichever replica ingress happened to route the
Superadmin request to.

**Authentication claims did not go through that abstraction at all.**
`Helpers/UserClaimsTransformation.cs` cached the authenticated user's id,
organization and role in a raw `IMemoryCache` under `auth:user:entra:{id}` and
`auth:user:email:{email}`, with a one-hour absolute expiry.
`Helpers/UserClaimsCacheInvalidator.cs` removed those keys when a user's role or
identity changed. Both operated on the memory of the process that handled the
request. So when an administrator demoted a user, the replica that processed the
change forgot the old claims and every other replica kept authorizing that user
at the old role until its own hour elapsed. That is the defect this decision
exists to bound: a role change that took up to an hour to reach a replica that
did not serve it.

**The frontend edge cache that used to absorb some of this is gone.** ADR 0018
moved the frontend into an nginx container inside the same revision, and the
Vercel purge path was removed with it. `CacheEndpoints` now clears only what the
serving process owns and nothing else, which is honest but very little.

**Nothing distributed exists in Azure yet.** There is no Azure Cache for Redis,
no other cache resource, and no configuration key for one, in
`src/BE/infrastructure/` or in the live App Configuration store. Provisioning one
is a recurring cost and a capacity decision that belongs to the owner of the
Azure subscription, not to a code change. The application change therefore has to
be mergeable and deployable while that decision is still open.

**How a connection string would reach the API is already settled.**
`.github/workflows/aca-live-deploy.yml` passes `APP_CONFIG_ENDPOINT` into
`app.bicep`, which sets `Azure__AppConfiguration__Endpoint` on the `api`
container. `Configuration/InfrastructureConfiguration.cs` connects to that store
with the runtime managed identity and calls `ConfigureKeyVault`, so App
Configuration values and their Key Vault references are both resolved at startup.
`aca/runtimeDataAccess.bicep` grants the runtime identity `App Configuration Data
Reader` and `Key Vault Secrets User`. A new secret needs no new mechanism — only
a Key Vault secret, an App Configuration reference to it, and a restart.

### What `HybridCache` does and does not do across processes

This was measured, not assumed, because the decision depends on it. Two
`DefaultHybridCache` instances from
`Microsoft.Extensions.Caching.Hybrid` 10.6.0 — the version
`Workslip.Api.csproj` resolves, and the one that wins over the 10.1.0 pin in
`Workslip.Application` and `Workslip.Infrastructure` — were given one shared
`IDistributedCache` and observed at the L2 boundary:

- **L2 sharing works.** Instance A's `GetOrCreateAsync` wrote the entry to L2;
  instance B read that entry back from L2 instead of running its own factory. A
  cold replica is served from the shared cache.
- **Tag invalidation is published to L2.** `RemoveByTagAsync("all")` on A wrote
  an 8-byte tag-invalidation timestamp to L2 under the key `__MSFT_HCT__all`.
  Reads consult those markers (`__MSFT_HCT__*` and the per-tag key) before
  trusting an L2 payload.
- **Tag invalidation does not delete the payloads.** The marker carries a
  1000-day lifetime; the cached rows stay in L2 until their own expiry. Reads are
  meant to reject a row that predates the marker.
- **A tag's marker is read at most once per process, and never re-read.** After A
  invalidated the tag, B returned the stale value on every one of 40 reads over
  195 seconds and made no L2 call at all. B had already memoised that tag's
  "not invalidated" state and never refreshed it. Nor does B converge when its own
  copy lapses: measured separately, a replica whose one-minute local entry expired
  reloaded the same pre-invalidation payload from L2 and kept answering it at 60,
  70 and 80 seconds with no database read. In the assembly this is
  `DefaultHybridCache._tagInvalidationTimes`, a `ConcurrentDictionary<string,
  Task<long>>` populated with `TryAdd` on first use, with no expiry and no refresh
  timer; the wildcard marker is read once, in the constructor. **There is no
  backplane in this package** — no pub/sub channel — and no `HybridCacheOptions`
  setting that governs any of it.
- **Key removal does delete.** `RemoveAsync(key)` removes the L2 row and the
  calling process's L1 entry. It does not touch any other process's L1.

So an L2 buys cross-replica *sharing* and a cross-replica *delete by key*. It does
not buy cross-replica eviction of any kind: no operation in this package causes a
replica that is already running to drop an entry it holds. A tag invalidation is
never observed by such a replica at all, and a key delete is not either — it only
removes the shared copy the replica would otherwise have rehydrated from. Any
claim that registering Redis makes a Superadmin "clear cache" take effect on
every replica would be false, and this ADR does not make it.
`CacheReach.ClearReachesEveryReplica` is a compile-time `false` with a
characterization test behind it, so a package upgrade that changes the behaviour
surfaces as a test failure rather than as a quietly wrong runbook.

That absence of a backplane is the fact the rest of this ADR turns on. A cache
with no backplane can be shared; it cannot be told to forget something
everywhere. Any design that needs the second property has to get it from
somewhere other than this package.

## Decision

1. **The application caches through one abstraction.** Feature code uses
   `HybridCache`. `IDistributedCache` is registered only as `HybridCache`'s
   backing store; no feature code takes a dependency on it, and no new raw
   `IMemoryCache` cache regions are added. `IMemoryCache` remains available for
   genuinely process-local concerns that must not be shared.

2. **Redis is an optional second level, for the caches where staleness is
   benign.** When `Azure:Redis:ConnectionString` is configured the API registers
   Redis as `HybridCache`'s L2; when it is absent nothing distributed is
   registered — L1 only, no distributed dependency, no startup failure, no
   warning an operator must learn to ignore, and no Redis credential anywhere in
   the deployed system. What the shared tier is for is reference data, job lists
   and job reports: rows that are reconstructible from SQL, that several replicas
   would otherwise each load independently, and where a value a few minutes old
   is a display concern rather than a correctness one. Merging and deploying
   before any Azure cache resource exists is safe, and so is keeping it in
   production if the resource is never provisioned.

3. **Authentication claims are cached per process only, and nothing about a
   user's identity or role is written to the shared tier.** This is the part of
   the design that was tried the other way twice and abandoned, so the reasoning
   belongs here rather than in a commit message. A shared claims cache is only
   worth having if an invalidation cannot be overtaken: the moment a resolution
   that read the pre-change row can publish it *after* the invalidation deleted
   it, one revoked role is pinned in front of every replica for the shared row's
   whole lifetime — a far wider and longer exposure than the per-replica window
   the design was meant to shrink. This package has no backplane and no
   compare-and-set, so that guarantee has to be built on top of it. Two attempts
   were made and both were reproducibly defeated (see
   [Rejected alternatives](#rejected-alternatives) for the evidence): the second,
   an invalidation-generation token, was defeated four ways against a real Redis
   and also failed open, so a revoked role could be pinned deployment-wide for an
   hour with the invalidation itself recorded as a success. Each round closed one
   interleaving and the next round found another, which is the signature of a
   mechanism that over-reaches rather than one that needs a fourth patch. The
   thing being bought was one saved database read per replica per lifetime. The
   thing being risked was authorization correctness. Claims therefore stay in the
   process that resolved them, and the invalidation-generation token, the
   write-then-verify publish path and the question of how they should fail are
   all deleted rather than hedged.

   The claims cache stays on `HybridCache` — decision 1 admits no new raw
   `IMemoryCache` regions — and is held to L1 by
   `HybridCacheEntryFlags.DisableDistributedCache` on both its write options and
   its read options (`UserClaimsCache.EntryOptions` and `ProbeOptions`). Both
   halves of that flag do work: the write bit keeps the payload out of the shared
   tier, and the read bit keeps the key out of it, which matters because the key
   contains the user's e-mail address and a cache key is transmitted in the clear
   (decision 7). For the same reason `IUserClaimsCacheInvalidator` removes the
   entry from the container's `IMemoryCache` directly instead of calling
   `HybridCache.RemoveAsync`, which ignores entry flags and would send a delete
   for a key that is never written.

4. **What bounds a role change is the claims entry's local lifetime, and nothing
   else.** The entry lives for one minute (`UserClaimsCache.Lifetime` in
   `Helpers/UserClaimsTransformation.cs`). On the replica that served the change
   the effect is immediate, because `IUserClaimsCacheInvalidator` removes that
   process's own entry — which is the whole of that type's job now. On every
   other replica the old role is still honoured until that replica's own copy
   lapses and it re-resolves from the database. The clock starts when that
   replica last populated its copy and not when the change happened, so the worst
   case is a full lifetime after the change. Measured on the earlier shared
   design at the same one-minute lifetime: the demotion and its
   invalidation landed 31.0 s into a warm replica's entry, and that replica
   changed its answer 29.1 s later — at its own expiry, not at the invalidation —
   with one extra database read. **There is no immediate cross-replica
   revocation, and this ADR does not provide one.** Getting it requires either a
   backplane that tells every replica to drop the entry, or a shorter
   authentication token lifetime so a revoked principal stops presenting a valid
   credential. It does not require, and cannot be obtained from, another cache
   tier. If prompt revocation becomes a requirement, that is its own decision with
   its own ADR.

5. **No authorization behaviour depends on whether Redis is configured.** Claims
   are cached the same way, for the same lifetime, in both configurations, so the
   role-change bound in decision 4 is the same number on every environment and an
   operator never has to ask which configuration a process is in before reasoning
   about authorization. That is a deliberate simplification of the earlier
   design, in which the claims lifetime was picked from whether an L2 existed and
   every incident had to establish the configuration first. What a connection
   string changes is confined to the benign regions in decision 2.

6. **A distributed cache is a cache, never a source of truth, and never a
   dependency of authentication.** A Redis that is unreachable, slow or wiped
   degrades the API to L1 behaviour. It must not fail a request, must not fail
   startup, and must not prevent a user from authenticating. Cache backends fail;
   authentication that depends on one inherits its availability. With claims out
   of the shared tier this is easier to hold than it was: the authenticated
   request path no longer touches Redis at all.

7. **Personal data must not become cache key material.** A cached *value* is
   serialized, and in this system it is also small and non-identifying by
   construction. A cache *key* is a key name: it is transmitted in the clear to
   the cache server, and it is what `redis-cli --scan` prints, what a slow-log
   entry records, and what a provider exception quotes back into a log or a
   diagnostics response. So a key gets none of the protection a value gets, and
   the value's own safety says nothing about the key's. Keys derived from user
   input must therefore carry only identifiers and hashes of the search terms,
   never a customer name, e-mail address or postal address in plaintext. Where
   that is enforced is recorded in
   [`Docs/operations/CACHE_DIAGNOSTICS.md`](../../operations/CACHE_DIAGNOSTICS.md).

8. **This ADR does not provision Redis.** No Azure Cache for Redis resource is
   added to a Bicep template here. What would have to be provisioned and wired —
   the resource, the tier question, how the connection string reaches the
   container app, and what changes in the deploy workflow — is written down for
   an operator in
   [`src/BE/infrastructure/README.md`](../../../src/BE/infrastructure/README.md).
   Creating the resource is an owner decision with a recurring bill.

9. **The secret follows the ADR 0001 lifecycle.** If Redis is provisioned, its
   connection string is a Key Vault secret plus an App Configuration Key Vault
   reference, resolved by the runtime managed identity. It is not a plaintext
   environment variable in `aca/app.bicep`. The SQL connection string may sit in
   a plain container env var because it is passwordless and carries only an
   identity client id; a Redis connection string carries an access key, and
   anyone who can run `az containerapp show` would be able to read it.

10. **Operational documentation states the boundary, not the aspiration.**
    [`Docs/operations/CACHE_DIAGNOSTICS.md`](../../operations/CACHE_DIAGNOSTICS.md)
    says which layer a clear reaches and which it does not, in both the
    Redis-absent and Redis-present configurations, so an operator is not misled
    into believing a clear was global when it was not.

## Consequences

### Positive

- The role-change window is bounded at one minute per replica in every
  configuration, down from the hour the raw `IMemoryCache` entry used, and the
  bound does not depend on any infrastructure being provisioned.
- **No authorization data reaches a shared store.** A Redis that is compromised,
  wiped, or restored from a snapshot taken before a demotion cannot change who
  is authorized as what, because it never held that answer. This also removes
  persistence from the list of things the cache tier has to get right.
- The authenticated request path touches only in-process memory and, on a miss,
  SQL. It cannot be slowed by a cache outage, which is what made the cold-connect
  cost below cheap to accept.
- One abstraction means one place where expiry, tagging, serialization and
  instrumentation are decided, and one place a future backplane could be added.
- Reference data and job lists survive a scale-from-zero cold start when Redis is
  configured, because a new replica reads L2 instead of the database — within the
  shared entry's own lifetime, which for those regions is the library's 5-minute
  default.
- The change carries no infrastructure prerequisite. It ships on its own
  schedule, and the Azure cost decision is taken separately with the evidence to
  support it.
- Nothing in the deployed system holds a Redis credential until someone
  deliberately creates one.

### Trade-offs

- **The defect in the context above is bounded, not closed.** A revoked role can
  still be honoured for up to one minute by a replica that did not serve the
  change, in every configuration. That is a deliberate acceptance, not an
  oversight:
  closing it needs a backplane or a shorter token lifetime (decision 4), and
  neither was in scope here.
- **Claims are re-read from the database once per active user per replica per
  minute.** At the previous one-hour expiry it was once per hour, so this is
  sixty times the claims lookups it used to make, on the authenticated request
  path, with nothing shared to absorb them. That read is a table scan, not a
  keyed seek: `Users` carries only the clustered primary key and an index on
  `(OrganizationId, Id)`, and `IUserRepository.GetByExternalIdentityAsync` ORs
  `EntraId` against `LOWER(LTRIM(RTRIM(Email)))` and the same over `EntraEmail`,
  which is not sargable — the measured plan is a clustered index scan. So the
  cost is `active_users x replicas / lifetime` scans per second, and the number
  to watch is the `Users` row count rather than the request rate: cheap at
  hundreds of rows, linear from there. Persisted computed `NormalizedEmail` /
  `NormalizedEntraEmail` columns with one index per `OR` branch would make it a
  seek and turn the lifetime into a pure revocation-window decision; that is a
  follow-up, not part of this change.
- **The clear action stays local to the serving replica, and the documentation
  has to say so.** With Redis configured, a Superadmin clear empties the serving
  process and publishes tag invalidations; it does not delete the shared payloads.
  Another replica keeps its own copy until that copy expires, and then reloads
  the same pre-clear payload from L2, because it never re-reads the invalidation
  marker (measured: unchanged answers at 60, 70 and 80 seconds with no database
  read). Waiting does not converge such a replica. An operator who needs a hard,
  everywhere reset restarts the revision, which is the only step that empties
  every process.
- **A second data store, when enabled, is a second thing that can fail.**
  Decision 6 bounds the blast radius to degraded cache behaviour, and no failure
  shape took authentication down when this was measured — cache absent,
  configured but dead at startup, and killed mid-request all kept answering
  authenticated requests with 200. The residual cost is latency, and it
  concentrates in one place: a process that has never reached the cache pays the
  connect timeout on its first cache-touching request: sub-half-second when the
  endpoint refuses the connection (measured 0.362 s), up to ~6.4 s when it
  black-holes and the read and the write each wait out the two-second
  `ConnectTimeout` with `ConnectRetry=3` across a cold multiplexer. An outage that begins after a connection has existed stayed
  under 200 ms per request. `BacklogPolicy.FailFast` is what keeps the second
  case cheap; it does not shorten the first.
- **For some regions an L2 lengthens worst-case staleness.** Job lists and job
  reports set only `LocalCacheExpiration` (15 seconds and 1 minute). With an L2
  they also get the library's default 5-minute `Expiration`, and because a tag
  invalidation from another replica is never observed, such a replica can
  repopulate the stale row from L2 after its own copy expires. The tightest
  honest statement is that enabling a distributed cache improves cross-replica
  *coherence of what is cached* while, for those two regions, widening the window
  in which a stale value can be served. That trade is acceptable precisely
  because those regions are the benign ones; it is the same trade that was not
  acceptable for claims.
- **Serialization becomes a compatibility surface.** Entries in L2 outlive a
  deployment. A changed cached type can be read back by a new revision as the
  old shape, which an L1-only cache never had to consider. Claims are exempt,
  because they never enter L2.
- **Version skew is now load-bearing.** `Workslip.Api.csproj` pins
  `Microsoft.Extensions.Caching.Hybrid` 10.6.0 while `Workslip.Application` and
  `Workslip.Infrastructure` pin 10.1.0. NuGet resolves the graph to 10.6.0 today,
  which is the version the behaviour above was measured on. Dropping the API pin
  would silently change which implementation runs.

## Rejected alternatives

- **Publish authentication claims to the shared tier so an invalidation reaches
  every replica.** This was the design of two earlier revisions of this ADR, and
  it is recorded here in full because it is the obvious idea and it will be
  proposed again. The shape was: move the claims cache onto `HybridCache` so
  `IUserClaimsCacheInvalidator` deletes the shared row as well as the local one,
  shorten the local lifetime because a lapsed entry can then refresh from the
  shared row instead of the database, and rely on the by-key delete to stop the
  shared tier from handing the pre-change row back out. It was rejected on
  measurement, on three separate counts.

  1. **It did not buy the window it was credited with.** The first verification
     round measured the shortened local lifetime doing all the work on its own: a
     warm replica converged at its own L1 expiry via a database read (its
     database-call count went 1 → 2), and the shared tier contributed nothing to
     the timing. The claim that "both halves are required" was measured false.
     What the shared half actually bought was one saved database read per replica
     per lifetime.
  2. **The lost-update race was real, and reproducible.** A claims resolution
     that had already read the pre-change role from the database, but had not yet
     written it, wrote it to L2 *after* the invalidation deleted the rows. The
     poisoned row carried the full one-hour distributed lifetime, and a
     brand-new replica then resolved the old role from it with no database read
     at all. Ordering the delete before the administrative response does not
     prevent this: it orders only against later requests on the same replica, not
     against a resolution already in flight anywhere else.
  3. **The guarantee could not be rebuilt on top of the cache.** The second
     attempt added an invalidation generation — a process-local counter plus a
     token in the shared tier, stamped before the delete, captured before a
     resolution's database read and re-checked after its write, with the write
     withdrawn if the token moved. The second verification round reproduced the
     lost update through it four ways against a real Redis, and found that it
     also failed open: when the shared half of the token could not be read the
     check fell back to the process-local counter, so a revoked role could be
     pinned deployment-wide for the shared row's full hour while the invalidation
     was recorded as a success and logged as one. Every round closed one
     interleaving and the next round found another.

  The root cause is structural rather than a bug in either attempt. A
  shared claims cache needs an invalidation that cannot be overtaken. This
  package has no backplane and no compare-and-set, so such a guarantee has to be
  built out of ordinary cache reads and writes on a store that is explicitly
  allowed to be unreachable — and a token that lives in the same fallible store
  it is meant to protect cannot be more reliable than that store. The cost of
  being wrong is an authorization decision, and the benefit was a saved database
  read. **Do not re-propose a shared claims cache without a backplane.** With
  one, this becomes a different and much smaller design: the backplane, not the
  shared row, is what would carry the invalidation.

- **Keep the one-hour claims lifetime.** The status quo before this ADR. It makes
  the claims lookup as cheap as it can be and leaves the original defect exactly
  as it was — up to an hour of a revoked role being honoured on every replica
  that did not serve the change. Rejected because that window is the defect, and
  because the read it saves is a single indexed row.
- **Register Redis and claim the Superadmin clear is now global.** Rejected on
  the measurement above: `RemoveByTagAsync` publishes a marker, it does not evict
  another process's L1. Shipping that claim in the runbook would send operators
  to a button that does not do what it says during an incident.
- **Provision Azure Cache for Redis in `aca/foundation.bicep` as part of this
  change.** Rejected because a cache resource is a recurring monthly cost and a
  capacity choice, and the person who owns the subscription has not made it. It
  would also couple a code change to a resource creation, so a rollback of the
  code would leave a billed resource behind.
- **Make the connection string a required setting.** Rejected because it makes
  the application undeployable until the infrastructure decision is taken, and it
  turns an unreachable cache into an outage. It would also break local
  development and CI for everyone who has no Redis running.
- **Use `IDistributedCache` directly in feature code.** Rejected because it gives
  up stampede protection, tagging and the existing cache instrumentation, and
  splits caching policy across two abstractions.
- **Put claims in a signed token or a cookie so no server-side cache is needed.**
  Rejected because the Workslip role is server-owned state that must stay
  revocable; embedding it in a bearer credential makes the revocation window the
  token lifetime and removes the ability to invalidate at all. Note that a
  shorter token lifetime is still one of the two ways to get prompt revocation
  (decision 4) — what is rejected here is moving the role itself into the token.
- **Add a Redis pub/sub backplane of our own to force cross-replica L1
  eviction.** Rejected as out of scope rather than as a bad idea, and it is the
  honest answer to "we need revocation to be immediate". It is real work, it
  duplicates something the cache library may add, and it would have to be correct
  under partition and restart in a way the generation token was not. Decision 4's
  local lifetime bounds the window in the meantime.
- **Run Redis as a container inside the Container Apps environment to avoid the
  managed-service bill.** Rejected for production: a cache with no persistence,
  no failover and no patching story, sharing the app's own scaling boundary, is
  not cheaper once it is on call. It is exactly right for local development,
  which is where `docker-compose.yml` runs it.
