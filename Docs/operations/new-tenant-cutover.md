# New-tenant production cutover

**Status:** Tenant migration completed — retained as the executed procedure and the rollback reference  
**Owner:** Workslip repository owner  
**Source of truth:** production workflows, `src/BE/infrastructure/main.bicep`, this runbook, and exact-SHA run evidence  
**Review cadence:** before every tenant migration rehearsal or production cutover

This runbook moves Workslip from the current production boundary to the new
tenant without treating a green deployment as proof that production data,
identity, email, files, and rollback are ready.

## Current state

The `prod` → `live` tenant migration is **done**. Customer traffic is served from
the new tenant, and this runbook is now history plus the rollback reference
rather than a plan. Two things follow from that, and conflating them is the
mistake this section exists to prevent.

**The serving path today:**

```text
browser → Vercel → api-mrsoftwarev2-live (App Service, rg-mrsoftwarev2-live, Sweden Central) → db-mrsoftwarev2-live
```

`src/FE/vercel.json` proxies `/api/*` to
`api-mrsoftwarev2-live.azurewebsites.net`, and `backend-production-deploy.yml`
deploys that App Service as `AZURE_WEBAPP_NAME`. That is the production backend.

**What is completed:**

- new-tenant foundation reconcile (`mrsoftwarev2` / `live`, Sweden Central);
- cross-tenant SQL database copy into `db-mrsoftwarev2-live`, plus the guarded
  seed and runtime-identity/SID reconciliation;
- Entra B2B guest migration to the new tenant and the reconciliation workflow;
- API deployment to `api-mrsoftwarev2-live`;
- Vercel proxy cutover to the new tenant.

**What is *not* completed — do not describe this as finished:**

The Container Apps track is staged but is **not** the production path. The
full-stack container app `ca-workslip-live-app` (environment
`cae-workslip-live-app`, domain `app.mrsoftware.dk`) is built and deployed by
`aca-live-deploy.yml`, and `aca-live-cutover.yml` still exposes its `bind` and
`retire` modes as unexercised manual gates. That workflow names
`api-mrsoftwarev2-live` as `LEGACY_WEBAPP`, so the App Service is retired only
after the ACA domain bind is approved and verified. Until then the App Service
is live infrastructure, not legacy in practice.

Because the ACA path is a distinct runtime cutover, the traffic-cutover,
rollback, and evidence requirements below still apply to it. Reaching the new
tenant did not consume those gates.

**Rollback boundary reminder:** customer writes are already accepted by
`db-mrsoftwarev2-live`. The simple "point the proxy back" rollback described at
the end of this document expired when that happened. See
[Rollback boundary](#rollback-boundary).

## Fixed boundaries

Both columns are recorded because the former boundary remains the rollback
reference. "Former production" is no longer serving customers.

| Boundary | Former production | New tenant (serving) |
|---|---|---|
| GitHub environment | `prod` | `live` |
| Company/environment | `mrsoftware` / `prod` | `mrsoftwarev2` / `live` |
| Azure region | West Europe | Sweden Central |
| Resource group | `rg-mrsoftware-prod` | `rg-mrsoftwarev2-live` |
| API | `api-mrsoftware-prod` | `api-mrsoftwarev2-live` |
| SQL database | `db-mrsoftware-prod` | `db-mrsoftwarev2-live` |
| Blob account | `stmrsoftwareprod` | `stmrsoftwarev2live` |
| Frontend traffic | Vercel proxy to `api-mrsoftware-prod` | Vercel proxy to `api-mrsoftwarev2-live` |

Names are selected from this allowlist by the workflow. They are not free-form
dispatch inputs.

## Approval contract

A typed workflow confirmation is an additional guard, not owner approval.
Every mutating phase also needs the repository owner's explicit approval in the
delivery thread and approval of the selected protected GitHub environment.

| Phase | Mutation | Required workflow confirmation |
|---|---:|---|
| Infrastructure preview | No | `PLAN NEW TENANT` |
| New-tenant foundation reconcile | Yes, new tenant | `RECONCILE NEW TENANT` |
| Production data freeze and copy | Yes, both boundaries | phase-specific confirmation in the reviewed data-move procedure |
| API package deployment without traffic | Yes, new tenant | `DEPLOY NEW TENANT AFTER DATA VERIFIED` plus the reviewed manifest SHA-256 and evidence URL |
| Vercel proxy cutover | Yes, customer traffic | separate cutover approval |
| Rollback | Yes | explicit rollback approval unless responding to an active incident |

Merging a PR to `main` is itself a production action: current production backend
and frontend automation run for the merged exact SHA. Do not merge a cutover PR
until that consequence is explicitly approved.

## Environment boundary verification

GitHub inspection on 2026-08-23 confirmed that the `live` environment exists
with the required reviewer, administrator bypass disabled, and exactly the
`main` deployment branch. The workflows still call
`verify-deployment-environment.mjs` before any job can reference that
environment; the check requires:

- the environment already exists;
- exactly one deployment branch policy, for `main`;
- repository owner `rasm105k` (GitHub user ID `31623093`) as a required
  environment reviewer; and
- administrator bypass disabled.

This prevents GitHub Actions from silently weakening or recreating the `live`
boundary. The environment currently has the tenant/subscription secrets and
the infrastructure client variable required for foundation reconcile. The
application deployment secret `AZURE_CLIENT_ID` and reviewed SQL/blob manifest
remain separate hard gates before the API package can be dispatched.

## First run after repository approval

*Executed. Retained as the rehearsal procedure for any future tenant move.*

After the cutover-control PR is explicitly approved for merge and `live` is
configured, run `Infrastructure · Production reconcile` with:

- operation: `plan`
- target: `new-tenant`
- confirmation: `PLAN NEW TENANT`

The workflow verifies exact green `main` before acquiring Azure credentials,
selects the protected `live` environment, verifies its tenant and subscription,
and calls `plan.ps1`. The script has no input that enables mutation.

Review the complete output. In particular, block reconcile if it proposes:

- deletion or replacement of data-bearing resources;
- movement outside Sweden Central;
- an unexpected App Service plan SKU change; the retained F1 compatibility path
  intentionally has no staging slot and deploys directly;
- a new processor, region, or unexpected public-access change;
- removal of Key Vault purge protection, storage soft delete, diagnostics, or
  production identities.

An absent `live` GitHub environment or any missing
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_CLIENT_ID`, or
`AZURE_INFRA_CLIENT_ID` is a hard stop. Initial identity bootstrap remains the
local privileged administrator step documented in
[new-tenant manual steps](new-tenant-manual-steps.md).

## Foundation reconcile

Only after the preview has been reviewed and owner approval is recorded, run the
same workflow with:

- operation: `reconcile`
- target: `new-tenant`
- confirmation: `RECONCILE NEW TENANT`

This reconciles Azure resources and the dedicated database-migration identity.
It deliberately does not declare the new API ready. API code, copied data,
tenant-bound user IDs, ACS verification, and frontend traffic remain separate
gates.

## Data migration

### Rules

- Production data never enters GitHub artifacts, workflow logs, PR text, or
  local files that are not encrypted and immediately destroyed.
- Record only non-personal evidence: table names and counts, blob counts and
  byte totals, timestamps, operation IDs, migration IDs, and hashes of the
  evidence manifest.
- Keep the current production stack intact until the acceptance window closes.
- The final database copy is taken while source writes are stopped. A BACPAC
  export while writes continue is not transactionally consistent.
- Do not apply schema migrations to an empty target and call the result ready.

### Database

Use an Azure SQL transactionally consistent database copy for the final
snapshot. Microsoft documents cross-tenant copy through T-SQL only, using a SQL
authentication login on the target. The portal, Azure CLI, and PowerShell copy
commands do not support a different subscription.

The reviewed operator procedure must:

1. verify both exact tenant and subscription IDs;
2. stop the current API before the final snapshot so no writes can race it;
3. create one random, short-lived SQL login with the same name, password, and
   SID on both logical servers;
4. start the copy from the target server into a timestamped database name;
5. wait for `ONLINE` and compare non-personal per-table row counts;
6. preserve the pre-existing target database under a timestamped quarantine
   name, then move the verified copy to `db-mrsoftwarev2-live`;
7. reconcile the new API and migration database principals;
8. delete the temporary login from both servers and clear its secret material;
9. retain the current production database and automated backups for rollback.

Do not use a BACPAC as the backup/restore proof. Microsoft describes BACPAC as a
schema-and-data movement format, not a backup mechanism. If database copy is
blocked, SqlPackage export/import is the fallback, but the encrypted BACPAC must
move directly through controlled Azure storage or an approved operator host and
must never be uploaded as a CI artifact.

References:

- [Copy an Azure SQL database](https://learn.microsoft.com/en-us/azure/azure-sql/database/database-copy?view=azuresql)
- [Export an Azure SQL database](https://learn.microsoft.com/en-us/azure/azure-sql/database/database-export?view=azuresql)

### Blob data

Copy every container, including `uploads`, `documents`, and any dynamic
`powerbi-*` container. Perform an initial copy before the outage window and a
final synchronization after source writes stop.

Microsoft Entra authorization for both sides of one AzCopy transfer requires
both accounts to be in the same tenant. These accounts are not, so use
short-lived, least-privilege SAS URLs generated separately in each tenant. Mask
them immediately, never echo them, never save AzCopy logs as artifacts, preserve
blob tags/metadata where present, and revoke or let the short expiry close the
access after verification.

Compare source and target container names, blob counts, byte totals, and failed
transfer counts. A successful process exit without those comparisons is not
data-migration evidence.

Reference:

- [Copy blobs between Azure storage accounts with AzCopy](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-blobs-copy)

## Tenant-bound identity and integrations

After the database is on the new boundary:

1. create or invite the corresponding users in the new Entra tenant;
2. run `backfill-entra-object-ids.ps1` without `-Apply`;
3. resolve every `Missing`, `Ambiguous`, `Conflict`, and `NoEmail` result;
4. apply the backfill only with separate approval;
5. re-run and require every user to be `Current`;
6. test sign-in and test-user offboarding;
7. require ACS Domain, SPF, DKIM, and DKIM2 verification and a delivered test
   message before traffic moves.

See [Entra tenant migration](entra-tenant-migration.md) and
[new-tenant manual steps](new-tenant-manual-steps.md).

## Deploy without traffic

> **Superseded by the completed migration.** This section described deploying
> the new-tenant API while the old tenant still served traffic. That state is
> over: `backend-production-deploy.yml` now sets `AZURE_WEBAPP_NAME` to
> `api-mrsoftwarev2-live`, so the automatic path deploys the new tenant and the
> old boundary is no longer a deploy target. The confirmation string and
> manifest gates below are retained because the same evidence discipline
> applies to the outstanding ACA runtime cutover.

Historically, the automatic `workflow_run` path in the backend production
workflow stayed pinned to the old production boundary while its manual dispatch
path targeted only the new tenant, so that reaching
`api-mrsoftwarev2-live/health` could never be achieved by quietly repointing
automatic deploys.

After the reviewed data comparison manifest exists, dispatch
`Backend · Production deploy` from `main` with:

- confirmation: `DEPLOY NEW TENANT AFTER DATA VERIFIED`
- data manifest SHA-256: the lowercase 64-character hash of that exact manifest
- data manifest evidence URL: the reviewed WOR-430 Linear reference or
  Workslip repository issue/PR containing the non-personal manifest

The workflow validates the evidence URL's allowlisted location and records it
with the hash. The protected `live` environment reviewer must verify that the
linked manifest content hashes to the supplied value before approving the job.
The workflow does not infer data correctness from the hash alone. The mutating
job receives Azure credentials only after that owner approval.

The new-tenant package deployment must:

- use the exact current green `main` SHA;
- use the protected `live` GitHub environment and its OIDC identity;
- run reviewed migrations against the verified copied database;
- deploy to `api-mrsoftwarev2-live` only;
- leave the Vercel proxy unchanged;
- require direct `/health` = 200 and unauthenticated `/api/auth/me` = 401;
- prove authenticated sign-in, tenant isolation, representative reads, blob
  reads, and ACS delivery before becoming eligible for traffic.

## Traffic cutover

Traffic movement is a separate decision. It consists of changing the Vercel API
proxy from the current API to `api-mrsoftwarev2-live` and deploying the exact
approved frontend SHA. Frontend ownership and browser evidence still apply.

Before approval, record:

- exact backend and frontend SHA;
- green production CI for that SHA;
- direct new-API readiness evidence;
- database and blob comparison evidence;
- Entra backfill evidence;
- ACS delivery evidence;
- named operator, start time, communication window, and rollback commander.

After the proxy changes, verify browser → Vercel → new API → copied SQL/blob data
→ response. A public health check alone is insufficient.

## ACA runtime cutover: what still gates it

The Container Apps move is a second traffic cutover and has not happened. Two
things about it are easy to get wrong, so they are recorded here rather than
left to be rediscovered during the window.

### The CNAME change is the cutover, and it is unguarded

`app.mrsoftware.dk` is the customer-facing production domain.
`aca-live-cutover.yml` in `prepare` mode only *prints* the CNAME and TXT records;
publishing them happens by hand at the DNS provider, outside the workflow and
outside every guard in it. The moment the CNAME moves, live traffic moves.

Azure cannot validate the custom domain until that record already exists, so
there is an unavoidable window where the production domain resolves to a
Container App that has not yet accepted the hostname and has no managed TLS
certificate. Arrivals in that window get a certificate error or a 404.

Publish the CNAME and dispatch `bind` inside one maintenance window, with a
named operator and a rollback commander, and revert the CNAME if `bind` fails.
The workflow's `prepare` summary carries the same sequence.

### The automated gate is liveness, not acceptance

Both `bind` and `retire` gate on `tools/release/post-deploy-smoke.sh`, which
checks three things: `/` = 200, `/health` = 200, `/api/auth/me` = 401.

That proves the app boots, serves the SPA, and rejects anonymous API calls. It
does **not** prove authenticated sign-in, database reads, tenant isolation, blob
reads, or ACS delivery — the evidence this runbook requires above before traffic
moves. The smoke script is a liveness check that happens to be the only
automated gate on the mutation; it is not this runbook's acceptance bar, and
passing it is not evidence that the ACA runtime is ready for customers.

Produce the acceptance evidence separately, by hand, against the Container App's
own FQDN before publishing the CNAME. It cannot currently be automated:
authenticated release testing is blocked pending isolated staging and approved
test auth, per [synthetic test identities](synthetic-test-identities.md).

### Retirement ordering

`retire` stops `api-mrsoftwarev2-live`, the App Service the Vercel proxy rewrites
`/api/*` to. Once the CNAME moves, `app.mrsoftware.dk` is served by the Container
App and that Vercel path carries no customer traffic, so stopping it is safe in
that order — but only in that order. Before retiring, confirm in Vercel that no
other domain, alias or preview deployment still routes to the App Service; that
configuration is not in this repository and cannot be checked from it.

## Rollback boundary

> **We are past the simple-rollback point.** `db-mrsoftwarev2-live` has been
> accepting customer writes since the proxy cutover, so restoring the Vercel
> proxy to `api-mrsoftware-prod` would fork or lose every write taken since
> then. The pre-write procedure immediately below is retained for the
> outstanding ACA cutover and for any future tenant move; it is **not** the
> current rollback path for the tenant migration. For that, use the
> post-write options further down.

Before writes are enabled on a new boundary, rollback is:

1. keep or restore the Vercel proxy to `api-mrsoftware-prod`;
2. restart current production if it was stopped;
3. verify current `/health` and the direct/proxied unauthenticated auth contract;
4. keep the failed new boundary isolated for diagnosis.

Once customer writes are accepted by the new database, switching traffic back
would lose or fork those writes. At that point, first stop new writes and choose
one of:

- fail forward on the new boundary; or
- execute a reviewed reverse database/blob transfer before switching back.

That point of no simple rollback must be called out explicitly when the service
is reopened.

## Evidence required for completion

- exact-SHA green CI and protected-environment approvals;
- reviewed infrastructure plan and successful reconcile;
- tested SQL backup/restore independent of the movement mechanism;
- source/target SQL table-count manifest;
- source/target blob-count and byte-total manifest;
- zero failed transfers;
- all Entra mappings current;
- ACS verified and delivered;
- direct new API and authenticated application evidence;
- Vercel proxy evidence;
- rollback rehearsal and named incident ownership.
