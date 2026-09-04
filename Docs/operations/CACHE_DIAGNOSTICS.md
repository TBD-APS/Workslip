# Cache diagnostics

**Status:** Active
**Owner:** Workslip maintainers
**Source of truth:** Runtime cache instrumentation, API endpoint registrations, frontend diagnostics page, the distributed-cache registration in `Configuration/ServiceConfiguration.cs`, and executable validation
**Review cadence:** When cache regions, expiry policies, cache invalidation behavior or the distributed-cache configuration change

## Purpose

Workslip exposes cache metadata to Superadmin so cache behavior can be inspected without exposing cached business data, authentication material, tenant identifiers, or full cache keys.

## Access

The diagnostics UI is available at `/superadmin/cache` and requires the existing Superadmin permission boundary.

Backend endpoints are `GET /api/superadmin/cache/status` and the two clear
endpoints listed under [Clearing caches](#clearing-caches).

All diagnostics responses use `Cache-Control: no-store`.

## Backend regions

| Region | Cache type | Local expiry | Distributed expiry | Instrumented behavior |
|---|---|---:|---:|---|
| `reference-data` | HybridCache | 10 minutes | 5 minutes (library default) | hit, miss, set, load duration, failure, invalidation |
| `authenticated-users` | HybridCache, shared tier disabled per entry | 1 minute | none — never written to the shared tier | hit, miss, set, load duration, failure, invalidation |

`reference-data` gains a shared second level as soon as a distributed cache is
configured, which is why it carries two lifetimes: the local one bounds how long
one replica can serve a stale value, the distributed one bounds how long the
shared row can be handed to a replica that reads it.

`authenticated-users` is process-local in every configuration, deliberately.
Its entries carry `HybridCacheEntryFlags.DisableDistributedCache` on both the
read and the write path (`UserClaimsCache.EntryOptions` and `ProbeOptions`), so a
user's id, organization and role are never written to the shared tier and the
e-mail-address-shaped cache key never appears in a command sent to it. Its one
lifetime is therefore the whole story, and the shared tier cannot lengthen or
shorten it.

Two things to know when reading this region during an incident:

- **A claims row in Redis would be a bug, not a cache state.**
  `redis-cli --scan --pattern '*auth:user:*'` returning anything means
  authorization data is being published to a shared store, which
  [ADR 0019](../architecture/adr/0019-single-cache-abstraction-with-optional-distributed-second-level.md)
  decision 3 forbids. This is the authoritative check, and it is worth running
  after any change to the claims cache.
- **This region's `tier` and `clearScope` can be read as statements about
  claims.** The region is registered against `HybridCache` — that is the store —
  but declared `CacheEntryReach.ProcessLocal`, and `CacheReach.TierFor` /
  `ClearScopeFor` in `Workslip.Application/Common/CacheReach.cs` require both
  halves. So it reports `LocalOnly` and `ProcessOnly` in every configuration,
  including one where a distributed cache is configured and reachable. The
  keyspace scan above corroborates that independently of the labels; run it after
  any change to the claims cache rather than in place of reading them.

The claims cache used to be a raw `IMemoryCache` with a one-hour absolute expiry.
The lifetime is now one minute, and that lifetime — not the invalidation call — is
what bounds how long a revoked role can still be honoured on a replica that did
not serve the change. There is **no immediate cross-replica revocation**: the
replica that serves the role change drops its own entry at once, every other
replica keeps honouring the old role until its own copy lapses and it re-resolves
from the database. The clock starts when that replica last populated its copy,
not when the change happened, so the worst case is a full minute after the
change. The clear button does not shorten that; restarting the revision is the
only action that drops cached claims on every replica at once — see
[what a clear reaches](#what-a-clear-reaches-and-what-it-does-not).

Job lists and job reports also cache through `HybridCache` (15 seconds and 1
minute local respectively) but are not reported as separate regions. What
enabling Redis does to them is in
[a consequence worth knowing before enabling Redis](#a-consequence-worth-knowing-before-enabling-redis).

Counters are process-local and reset when the API instance restarts. They are
operational signals, not billing or audit records. On a multi-replica deployment
the status endpoint answers for whichever replica served the request, so two
consecutive reads can legitimately disagree.

## Is a distributed cache configured?

Worth answering early in a cache incident, because reference data and job lists
behave differently depending on the answer. It does **not** change how
authentication behaves: cached claims are process-local in both configurations
(see [Backend regions](#backend-regions)), so an authenticated request never
touches this cache. `GET /api/superadmin/cache/status` reports the state, and the
Superadmin page renders it:

| Reported state | Meaning |
|---|---|
| `NotConfigured` | No connection string. `HybridCache` runs L1-only and every replica caches independently. This is the state of every deployed environment until Azure Cache for Redis is provisioned. |
| `Reachable` | A distributed cache is registered and answered a read within the probe timeout. |
| `Unreachable` | A distributed cache is registered but did not answer. The API is still serving correctly, from L1 and the database, and the cache is degraded rather than down. |

Each region additionally reports its current tier (`LocalOnly` or
`LocalAndDistributed`) and how far a clear reaches (`ProcessOnly` or
`ProcessAndDistributedTier`). Both labels mean what they say for every region:
they are derived from the registered store AND the region's declared entry reach,
so a region whose entries opt out of the shared tier reports `LocalOnly` even
with a cache configured. What differs between regions is which ones genuinely
have a shared tier, not which labels are reliable — see
[Backend regions](#backend-regions).

`Unreachable` is not an outage. A distributed cache is an accelerator here, never
a source of truth and never an authentication dependency: reads and writes that
fail fall back to L1 plus the database, and the authenticated request path does
not read it at all. Measured with the cache killed underneath a running API: ten
consecutive authenticated requests all returned 200, the slowest in 76 ms, and
`/health` stayed 200. If the API is failing requests while this says
`Unreachable`, the cause is somewhere else — look there before restarting
anything.

It is not free either, and the cost lands in one specific place: **the first
request in a process that touches a cached region while the cache is unreachable
pays the connect timeout before it falls back**, once on the read and once on the
write. How long depends on how the endpoint fails: sub-half-second when it
refuses the connection outright (measured 0.362 s), up to ~6.4 s when it
black-holes and both the read and the write wait out the two-second
`ConnectTimeout` with `ConnectRetry=3` that the registration in
`Configuration/ServiceConfiguration.cs` sets. Every subsequent request in that
process is back to single-digit milliseconds. So expect one slow request per replica whenever a
process starts while the cache is unreachable: a scale-out, or a revision rollout
during a cache outage. It lands on whichever request first reads a cached region,
which is a reference-data or job-list read rather than the authentication itself.
Restarting the API repeats that request rather than curing it; the fix is a
reachable cache, or removing the connection string.

A cache that restarts underneath an already-connected process is cheaper than
that. Measured across a kill-and-restart cycle: nothing over 200 ms for the whole
15-second outage, and the only spikes — one request of ~1.5 s, then a few in the
200-300 ms range — arrived after the cache came back, while the multiplexer
reconnected. Writes resumed on their own; no request failed.

The status endpoint itself does not wait that long. Its probe is capped at
750 ms (`DistributedCacheProbe.DefaultTimeout`), so an unreachable cache is
reported as unreachable instead of holding the screen open — with the side effect
that a cold-but-healthy cache can read `Unreachable` for one poll and `Reachable`
on the next, because the connect finishes in the background.

The startup log is the other place to look, and it is authoritative about what
the process was given at boot:

```text
[STARTUP 06.1] Configure Redis distributed cache (HybridCache L2) - SKIPPED (not configured)
[STARTUP 06.1] Configure Redis distributed cache (HybridCache L2) - OK (1 endpoint(s), key prefix workslip:production:)
```

Configuration is read once at startup and never refreshed, so adding the key to
App Configuration does not affect a process that is already running. Provisioning
and wiring are described in
[the infrastructure README](../../src/BE/infrastructure/README.md).

## Historical metrics

When Application Insights is configured, the same safe aggregate signals are emitted as custom metrics:

- `workslip.cache.hit`
- `workslip.cache.miss`
- `workslip.cache.set`
- `workslip.cache.invalidation`
- `workslip.cache.failure`
- `workslip.cache.load_duration_ms`

The only custom property is `region`. A global clear emits the event `workslip.cache.global_clear`.

Example KQL for cache activity:

```kusto
customMetrics
| where name startswith "workslip.cache."
| extend region = tostring(customDimensions.region)
| summarize value = sum(valueSum) by bin(timestamp, 15m), name, region
| order by timestamp desc
```

## HTTP cache inspection

Cache-aware API responses include `X-Workslip-Cache` so HTTP behavior is visible directly in the browser Network panel:

- `miss` — the response representation was returned because no matching validator was supplied;
- `revalidated` — the supplied `If-None-Match` validator matched and the endpoint returned `304 Not Modified`;
- `bypass` — the endpoint explicitly uses `Cache-Control: no-store`.

A browser-served response that never reaches the API cannot be counted by the backend. Browser and PWA cache state is therefore inspected separately on the Superadmin page.

## Frontend diagnostics

The Superadmin page shows:

- React Query count, status, fetch state, staleness, observer count, and update time;
- service-worker registration state;
- Cache Storage names and entry counts;
- browser storage usage estimate when supported;
- backend cache counters and the API instance start/clear timestamps.

React Query keys are reduced to a safe top-level scope. Full keys and cached values are not displayed.

## What a clear reaches, and what it does not

Read this before using the clear button during an incident. The reach depends on
how `HybridCache` implements invalidation, and it is narrower than the button
suggests.

The behaviour below was verified against the installed
`Microsoft.Extensions.Caching.Hybrid` 10.6.0 assembly — the version
`Workslip.Api.csproj` resolves — by running two cache instances over one shared
distributed cache and observing every call at the distributed boundary:

- **`RemoveByTagAsync(tag)`, which is what the clear endpoint calls, does not
  delete anything.** It writes an invalidation timestamp to the shared key
  `__MSFT_HCT__<tag>` with a 1000-day lifetime. Cached payload rows stay where
  they are; reads are supposed to compare an entry against that timestamp and
  treat an older one as a miss.
- **A process reads a given tag's timestamp at most once, and keeps the answer
  for its whole lifetime.** So a replica that has already cached something under
  a tag never observes a later invalidation of that tag made by another replica.
  Measured: after one instance invalidated a tag, the second instance returned
  the pre-invalidation value on all 40 reads over 195 seconds, and made no call
  to the shared cache at all.
- **Waiting for the local copy to expire does not converge such a replica.**
  Measured separately: a replica whose one-minute local entry lapsed after a tag
  clear reloaded the same pre-clear payload out of the shared tier — because the
  payload is still there and the memoised marker still says "not invalidated" —
  and was still answering the old value at 60, 70 and 80 seconds with no database
  read. A restart is what converges it.
- **`RemoveAsync(key)` does delete, but only two copies.** It removes the shared
  row and the calling process's own copy. It does not evict any other process's
  copy, and there is no operation in this package that does. Cross-replica
  eviction would need a backplane, which this package does not have.
- **The serving replica is emptied twice over, which is why a cache outage does
  not break the clear.** The tag invalidation takes effect locally in the
  clearing process before its shared marker is written, so from that moment
  `DefaultHybridCache` rejects every L1 hit on a tagged entry — measured: against
  a dead cache `RemoveByTagAsync` threw and the local invalidation still took
  effect, the next read going to the database. Independently,
  `MemoryCache.Compact(1.0)` in the clear endpoint drops every entry that process
  holds, tagged or not, because `HybridCache`'s L1 *is* the registered
  `IMemoryCache` (verified by reference identity against
  `DefaultHybridCache._localCache`). Neither of the two reaches any other
  process.

### Reach, per operation

This table is the configured-cache shape, which is the one that surprises people.
With no distributed cache there is no shared tier to reach or to reload from, so
every "other replicas" cell collapses to the same sentence: each keeps its own
copy until that copy expires and then reloads from the database.

| Operation | Serving replica | Shared distributed tier | Other replicas |
|---|---|---|---|
| Superadmin/admin clear (`all` tag) | Cleared, by the local tag invalidation and the memory compaction | Invalidation published; rows stay until they expire | **Not reached.** Existing replicas keep serving their own copies and then reload the same rows from the shared tier when those copies expire. Only a restart converges them |
| Role/organization change for one user | Cleared — the user's claims entry is removed from this process | **Not reached, because nothing is there.** Cached claims are never written to the shared tier | Bounded by the local claims lifetime, one minute: each keeps honouring the old role until its own copy lapses, then re-resolves from the database. Only a revision restart, or a shorter lifetime, changes that number |
| Job list or job report change | Cleared | Invalidation published; rows stay | **Not reached**, same as the clear |
| Restarting the Container App revision | Cleared | Untouched | Cleared — every process starts empty, and a fresh process does read the invalidation timestamps, so previously-cleared rows are correctly rejected |

Two things follow that are easy to get wrong in an incident:

- **The reliable global reset is a clear followed by a revision restart.** The
  clear publishes the invalidations; the restart removes every replica's local
  copy. Either step alone leaves stale data on a replica that is already running.
- **The clear button is not a way to force fresh claims across a deployment, and
  neither is anything else on this page.** It empties the claims held by the one
  process that served the request. Every other replica is on the one-minute clock
  in the table above. A revision restart is the only action that drops cached claims
  everywhere at once.

### The endpoints say so themselves

Both responses carry the reach rather than leaving it to be inferred, so an
operator reading the raw response is not misled either. The clear response
includes the id of the instance that served it, the scope that was reached, a
`reachedEveryReplica` flag that is always `false`, whether the distributed tier
was marked invalid, and the distributed-cache state. Those fields are the part to
trust: the message names which of three outcomes happened, and this is what each
one leaves behind.

| Outcome | What was cleared | What is left, and for how long |
|---|---|---|
| No distributed cache configured | This process only | Every other replica keeps its own copy until that copy expires. There is nothing shared to clear, and nothing to repeat later. |
| Configured, distributed tier marked invalid | This process, plus an invalidation marker in the shared tier | The shared payload rows, until their own expiry. A process that starts from now on reads the marker and rejects them. A replica that is already running does not: it serves its own copy until that expires and then reloads the same rows from the shared tier. Waiting does not converge it — restarting the revision does. |
| Configured, distributed tier **not** marked invalid | This process only | The shared payload rows and no marker, so every process — new or already running — keeps being served them. |

The third outcome is the one to act on: the local clear happened, the shared tier
did not. It is returned as a success with an explanation rather than a `500`,
because a cache outage must not make an administrative clear look like a broken
endpoint — but it does mean the clear has to be repeated once the cache is back,
or the revision restarted.

None of the three outcomes concerns cached claims. Those are dropped on the
serving process in every outcome — by the local tag invalidation and the memory
compaction alike — and are not in the shared tier in any of them, so the
distributed half of the message says nothing about them either way.

### A consequence worth knowing before enabling Redis

Job lists and job reports set only a local lifetime — 15 seconds and 1 minute.
With no distributed cache, that is also the staleness bound. With one, the entry
also gets the library's default 5-minute shared lifetime, and a replica whose tag
state predates an invalidation can repopulate from that shared row. For those
regions, enabling a distributed cache can therefore *lengthen* worst-case
staleness on a replica that did not serve the change, from 15 seconds to about
five minutes.

Reference data is not affected: its shared lifetime (5 minutes) is shorter than
its local one (10 minutes), so a shared row cannot outlast the local bound.

Authenticated users are not affected either, and that is the point of keeping
them out: with no shared row there is nothing that can outlast the local
lifetime, so enabling or disabling Redis does not move the role-change bound in
either direction.

## Clearing caches

Backend endpoints:

- `POST /api/superadmin/cache/clear`
- `POST /api/admin/cache/clear` — retained for backward-compatible deployment
  automation

The clear action:

1. invalidates HybridCache entries tagged `all`, subject to the reach above;
2. compacts the process `IMemoryCache` — which is also HybridCache's L1, so this
   is what actually empties the serving replica, tags or no tags;
3. clears frontend React Query entries;
4. deletes browser Cache Storage entries.

There is no external edge cache to purge. The frontend is served by nginx from
inside the Container App revision (`src/FE/Dockerfile`, `src/FE/nginx.conf`), and
Azure Container Apps ingress does not cache responses. Freshness after a deploy
is a property of the cache-control policy in `src/FE/nginx.conf` — hashed
`/assets/*` filenames may be cached immutably, while `/index.html` and `/sw.js`
must revalidate — not of an operator-triggered purge. A stale client after a
release is therefore a cache-header bug in `nginx.conf`, and clearing caches from
this page will not fix it.

The service worker registration remains installed. Deleting its caches is sufficient for the next requests to repopulate current assets without removing PWA capability.

## Reproducing multi-replica behaviour locally

The local Docker Compose stack runs Redis and points the API at it, so
cross-process cache behaviour can be reproduced on a laptop instead of guessed
at in production. `docker compose up` and the cache status page will report a
configured, reachable distributed cache.

```bash
docker compose up -d redis                                                    # cache only
docker exec -it workslip-redis redis-cli --scan --pattern 'workslip:*'        # every cached key
docker exec -it workslip-redis redis-cli --scan --pattern '*__MSFT_HCT__*'    # tag invalidations
docker exec -it workslip-redis redis-cli --scan --pattern '*auth:user:*'      # must always be empty
docker exec -it workslip-redis redis-cli FLUSHALL                             # wipe it
```

Every key is prefixed `workslip:<environment>:`, so one Redis cannot be confused
between environments and the prefix is part of the pattern. Keys containing
`__MSFT_HCT__` are the tag-invalidation timestamps described above; watching one
appear is how you confirm a clear was published, and `FLUSHALL` is the local
equivalent of the restart that the deployed system needs.

The third pattern is a standing assertion rather than an inspection: cached
claims are process-local by design, so `*auth:user:*` must return nothing after
any number of authenticated requests. Run it after touching the claims cache
registration. Anything it prints means authorization data is being published to a
shared store, which
[ADR 0019](../architecture/adr/0019-single-cache-abstraction-with-optional-distributed-second-level.md)
decision 3 forbids.

The compose Redis is configured with no save points and no append-only file
(`--save "" --appendonly no`) and has no volume, so it writes nothing to disk on
its own and loads nothing at startup: `docker compose down` and `up`,
`docker compose restart redis`, and a host reboot each start it empty. It does
not restart when the API rebuilds, though: a `dotnet watch` reload keeps talking
to the same cache, so payloads written by the previous build — and any tag
marker, which carries a 1000-day lifetime — are still there to be read. After
changing a cached type, or when a stale invalidation is the thing being chased,
`FLUSHALL` or `docker compose restart redis` is what gives a clean slate. See the
comment on the `redis` service in `docker-compose.yml` for the one case that
defeats both.

To observe the single-replica default instead, remove the
`Azure__Redis__ConnectionString` line from the `api` service in
`docker-compose.yml`. The container may stay up; the configuration key is what
decides. That changes nothing about cached claims, which are process-local
either way. Details in
[Local full stack with Docker Compose](local-docker-compose.md).

## Security constraints

Three separate rules apply to a cache, and they are separate because each has its
own enforcement point: what a diagnostics response may contain, what a log line
may contain, and what a cache *key* may contain.

### What diagnostics must never return or render

- cached values;
- access tokens, invite tokens, secrets, or integration credentials;
- e-mail addresses or user identifiers;
- customer, job, report, or worksheet payloads;
- complete tenant-, user-, search-, or entity-specific cache keys;
- the distributed-cache connection string, its access key, or its host name.

The first five items hold by construction on these endpoints: what they return is
counters, region names, lifetimes, tiers, an instance id and timestamps. No cache
key, no cached value, no user identifier and no free text of their own. (The
redaction helper for free-text diagnostics, `DiagnosticsSanitizer`, belongs to
the error-reporting surface; the cache endpoints have nothing for it to clean.)

### The cache address is constructed, never redacted

The last item needs its own enforcement, because the text it would appear in does
not come from our own code: StackExchange.Redis names the endpoint it could not
reach in every connection message — `... UnableToConnect on <host>:<port>/Interactive, ...`.
An earlier revision of these endpoints returned exactly that, host and all. What
the distributed-cache status discloses is therefore deliberately narrow — whether
a cache is configured, whether it answered, a provider label, and a failure
reason:

- the **connection string and its access key** are never in a response.
  `Azure:Redis:ConnectionString` is read in exactly one place,
  `Configuration/ServiceConfiguration.cs`, once at startup, and no diagnostics
  path reads it back;
- the **provider label** (`provider`) is the registered implementation's type name
  (`RedisCacheImpl`, `MemoryDistributedCache`), which names the provider without
  saying where it lives;
- the **failure reason** (`error`) is constructed from the closed vocabulary in
  `DistributedCacheProbe.FailureReasons` — timed out, did not accept a
  connection, host name could not be resolved, rejected the credentials, or an
  unexpected error plus the exception's simple type name.
  `DistributedCacheProbe.DescribeFailure` reads the provider's exception only to
  classify it and returns none of its text; the appended type name is guarded by
  an ASCII-identifier check, so a value containing the dots of a host name or the
  punctuation of a connection string cannot pass it. Both the status endpoint
  (through `ProbeAsync`) and the clear endpoint (through `FromOutcome`) go through
  that one function, which is why there is no second path to keep in step.

Constructing the reason rather than redacting it is the deliberate part: a regex
over a provider message is a guess about where in the message the address sits,
and it fails in both directions — an earlier run had a loopback address survive
into a response as `[REDACTED_PHONE]:6399`, redacted by accident because it
matched a phone-number rule.

Those strings are a contract in both directions: the Superadmin screen maps them
to Danish copy (`describeDistributedFailure` in
`src/FE/src/features/superadmin/cacheApi.ts`) and falls back to a generic
sentence rather than rendering anything it does not recognise, so changing a
value on either side means changing the other.

### The same rule applies to logs

A diagnostics response is not the only place a cache address escapes. Anything on
a cache path that logs a provider exception logs its message too, and that
message carries the endpoint and the cache key of the operation that failed. So
**log the constructed reason from `DistributedCacheProbe.DescribeFailure`, not
`exception.Message` and not the exception object**, and name the configuration
key rather than its value. That applies to every logger on a cache path: the
claims cache, the clear endpoint, and the startup registration.

### Personal data must not become cache key material

This one is easy to miss because it inverts the usual instinct. A cached *value*
is serialized, and in this system it is small and non-identifying by
construction. A cache *key* is a key name, and it gets none of that protection:

- it is sent to the cache server in the clear, as part of the command;
- it is what `redis-cli --scan` prints, so anyone with cache access can enumerate
  the keyspace without reading a single value;
- it is quoted verbatim in provider exceptions and slow-log entries, which is how
  it ends up in a log line or, before the enforcement above, in an API response.

So a key derived from user input must carry identifiers and hashes of the search
terms, never a customer name, e-mail address or postal address in plaintext. The
enforcement point is the key builder itself — for the job list,
`BuildJobListCacheKey` in
`src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs`, whose inputs
include the free-text customer name, e-mail and address filters. Two properties
have to hold together: the key must not be reversible to the search term, and it
must still be distinct per search term, or two different searches would share a
cached result.

Whether both hold is a question for the keyspace and not for this document.
Check it against the local stack — after a filtered search, and after any change
to a cache key that takes user input:

```bash
docker exec -it workslip-redis redis-cli --scan --pattern 'workslip:*'
```

No key it prints should contain a name, an e-mail address or an address. Look in
particular at `jobs:list:` keys after searching by customer name, e-mail or
address, and at `auth:user:` keys, which should not be there at all. A key that
fails the scan is a defect in its key builder, not something to redact
downstream: by the time it is a key it has already been on the wire.

Frontend authorization is only a navigation control. The backend Superadmin policy is the security boundary.
