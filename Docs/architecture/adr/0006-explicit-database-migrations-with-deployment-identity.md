# ADR 0006: Explicit database migrations use a deployment-only identity

**Status:** Accepted  
**Date:** 2026-08-08  
**Issue:** WOR-367, amended by WOR-410 and WOR-413

## Context

Workslip production API startup now verifies database connectivity and does not need to create or modify schema. The existing SQL provisioning still granted the API runtime identity `db_ddladmin`, leaving an unnecessary schema-management capability on the ordinary web workload.

Future tenant-integrity and branch work requires reviewed schema/data changes. Those changes must be auditable, serialized and completed before application code that depends on them is deployed.

The first rollout of this model exposed an operational gap: if the protected production bootstrap had not been reconciled before the backend deployment ran, the deployment failed closed as designed but production remained on the previous API binary. Frontend releases could therefore advance beyond the production API contract until an operator manually reconciled infrastructure and the migration identity.

Local branch development has a different operational need. A branch may contain a new reviewed migration that the branch code already depends on. Requiring developers to remember a second manual migration command creates avoidable local version skew, but allowing general API-startup migrations would weaken the production safety boundary this ADR established.

A later local-runtime review found a separate safety gap: Development startup could inherit the production SQL target from Azure App Configuration and merely adapt managed-identity authentication to the developer's Azure identity. That made normal local application traffic capable of reaching production data even though remote automatic migrations were already blocked.

## Decision

Production database migrations are an explicit deployment operation.

- Versioned T-SQL files live under `src/BE/infrastructure/database/migrations`.
- CI validates migration filenames and transaction/batch constraints before a change is eligible to merge to `main`.
- The protected backend deployment applies pending migrations before the API package is deployed. Migration failure stops the release.
- A separate `Production database migrations` workflow may apply pending reviewed migrations manually without deploying the API or reconciling general infrastructure.
- The manual migration workflow may run only from `main`, requires the protected `prod` environment and an explicit `MIGRATE` confirmation, uses the same dedicated migration identity and migration runner as normal backend deployment, and shares the `azure-api-prod` concurrency group with backend deployment.
- If the dedicated migration identity is missing when the manual workflow starts, that workflow may use the existing protected production infrastructure identity only to run `reconcile-database-migration-identity.ps1`; it must then switch back to the dedicated migration identity before executing any migration.
- Applied migration IDs and SHA-256 checksums are recorded in `dbo.WorkslipSchemaMigrations`; an applied file is immutable.
- Migration checksums are canonical SHA-256 values over UTF-8 SQL after normalizing CRLF/CR line endings to LF, so Windows and Linux checkouts identify the same reviewed migration.
- A stored pre-canonical checksum may be reconciled only when it exactly matches the current migration bytes or the same current SQL rendered with CRLF line endings. Reconciliation updates only the stored `Sha256` under the migration lock and does not rerun migration SQL; every other mismatch fails closed.
- Migration execution uses a database application lock plus the existing production deployment concurrency gate.
- A dedicated user-assigned identity, `id-<company>-<environment>-migration`, owns production schema migration permissions.
- The migration identity receives only the database DDL/data roles required to perform reviewed migrations and SQL-server firewall management needed for the ephemeral GitHub runner connection.
- The ordinary API runtime identity retains normal database read/write access and must not be a member of `db_ddladmin`.
- The ordinary GitHub application-deployment identity may read the migration identity resource only to resolve its client ID; it does not inherit the migration identity's SQL permissions.
- Production and staging API startup remain limited to connectivity/readiness checks. Development seeding remains a separate explicit opt-in.
- If a production backend deployment fails and known bootstrap prerequisites are absent, a separate protected recovery workflow may use the existing production infrastructure identity to run the same authoritative production infrastructure reconciliation and migration-identity reconciliation used by the manual production workflow, then rerun only the failed backend deployment jobs.
- The recovery workflow must no-op when the known bootstrap prerequisites are already present so unrelated backend deployment failures are not hidden, retried or turned into infrastructure mutations.
- Recovery publishes a commit status for the failed production revision so the bootstrap decision/result can be inspected without relying on chat or runner-local logs alone.

### Local Development exception

Normal `Development` application startup is local-database-only. The effective `Azure:Sql:ConnectionString` must parse to a provably local SQL Server target: localhost/loopback, `.`, `(local)`, a local SQL Server instance or LocalDB. Remote, Azure SQL, LAN and ambiguous targets are rejected before the application host is built.

`appsettings.Development.json` is a tracked, secret-free safety baseline. It intentionally contains no production App Configuration endpoint and no production SQL target. Machine-specific values and secrets belong in ignored `appsettings.Local.json`, environment variables or explicit command-line overrides. Local configuration is reapplied after any shared Azure configuration so a production SQL value cannot win normal Development precedence accidentally.

When a local target is configured, `Development` startup may apply the same versioned migration files automatically. The local startup runner preserves the production migration semantics that matter for correctness: lexical migration order, `dbo.WorkslipSchemaMigrations`, canonical SHA-256 verification, the narrow existing LF/CRLF checksum reconciliation, one transaction per migration and the database application lock. It does not create a second migration format or edit production migration history.

This path is intentionally fail-closed:

- non-Development environments never use local auto-migration;
- ordinary Development startup rejects remote or ambiguous SQL targets entirely;
- `Workslip:ApplyLocalMigrations=false` disables local auto-migration but does not weaken the local-only SQL boundary;
- OpenAPI generation remains database-free and does not evaluate or execute local migrations.

The existing explicit `bootstrap-superadmins` operator command is the only current Development-mode exception that may intentionally target remote SQL. The operator must explicitly supply the approved Azure App Configuration endpoint or equivalent remote configuration, and the command preserves the existing developer-Azure-identity adaptation for a managed-identity SQL connection. That command exits after the scoped platform-identity operation and does not become a normal local HTTP-development path.

This is a developer-workstation safety boundary, not a deployment mechanism. It does not change the production migration identity, workflow, permissions or release ordering.

## Operational rules

Migration files are forward-only release artifacts. They are not silently edited after production application. A checksum mismatch fails closed unless the stored value is proven to be a pre-canonical LF/CRLF representation of the exact current migration; that narrow bookkeeping reconciliation never reapplies migration SQL and preserves `AppliedAt`/`AppliedBy`.

A destructive or material data-transforming migration must document its production-data preconditions, backup/restore expectation, expected locking/availability impact and recovery/forward-fix strategy in the owning PR/release evidence before execution.

Automatic down-migration is deliberately not part of production rollback. Application rollback is allowed only when the resulting schema remains compatible; otherwise recovery is an explicit database operation.

Normal production delivery remains the preferred migration path: reviewed migrations merge to `main`, the backend deployment applies them, then the API package is deployed. The manual `Production database migrations` workflow is for explicit operator-controlled migration execution when the database must be advanced independently of an application deployment; it never runs arbitrary SQL supplied as workflow input.

The production infrastructure identity remains deployment-only. Normal API runtime never receives or depends on that credential. The manual migration workflow may use that identity only when the dedicated migration identity itself is missing, and only for the authoritative migration-identity reconciler. General production infrastructure reconciliation remains a separate operational workflow. Recovery is allowed only inside the protected `prod` GitHub environment and reuses `deploy-infrastructure.ps1` plus `reconcile-database-migration-identity.ps1` so resource configuration, role assignments and identity ownership still have one authoritative implementation.

## Consequences

### Positive

- The API runtime identity no longer carries production schema-management rights.
- Schema changes are visible in deployment logs and happen before incompatible application code is released.
- Concurrent production migration attempts are controlled.
- Future schema work such as WOR-160 and WOR-364 has one durable rollout mechanism instead of adding production startup mutation.
- A developer switching to a branch with pending schema work can bring a local database forward by starting the API, reducing branch/code schema skew.
- Normal local Development cannot silently read or mutate Azure SQL because remote SQL is rejected before application startup, not merely excluded from the migration runner.
- A tracked safe Development baseline makes the local-vs-production boundary reviewable in Git while machine-specific secrets remain untracked.
- Operators can intentionally advance the production schema from reviewed `main` migrations without coupling that action to an API package deployment.
- The manual migration path can repair its own missing dedicated migration identity without requiring a separate full infrastructure deployment first.
- A missed production bootstrap no longer leaves production indefinitely on a stale API revision after later green `main` releases.
- Recovery remains narrow: it runs only after a failed backend deployment, mutates infrastructure only when known bootstrap prerequisites are incomplete, and reruns only that failed deployment.
- Recovery status is visible on the affected commit, making production bootstrap failures distinguishable from unrelated backend deployment failures.
- Migration identity is stable across Windows and Linux runner line-ending materialization without weakening immutable migration detection.

### Trade-offs

- Developers must configure a real local SQL connection in ignored local configuration or an environment variable before normal Development startup can run.
- Developers who already have an ignored `appsettings.Development.json` must move machine-specific values to `appsettings.Local.json` once when adopting the tracked baseline.
- The explicit `bootstrap-superadmins` operator path must supply its approved remote configuration deliberately instead of inheriting production settings from ordinary Development configuration.
- Development startup can mutate schema, but only on a connection target verified as local. Developers still need a supported local SQL Server and a local connection string; this ADR does not provision one.
- The local C# runner duplicates a small amount of migration execution mechanics from the production PowerShell runner. Both deliberately share the migration file/history/checksum contract; changes to that contract must keep the two implementations aligned.
- The protected recovery workflow needs access to the existing production infrastructure OIDC identity, but only for the incomplete-bootstrap repair path; normal backend deployment and application runtime keep their narrower identities.
- The manual production migration workflow also needs conditional access to the production infrastructure OIDC identity solely to create/reconcile the dedicated migration identity when that identity is absent; migration SQL still runs only as the dedicated migration identity.
- The manual production migration workflow adds an explicit operator path that can advance schema before the matching application release; migrations therefore remain responsible for forward compatibility when that path is used.
- When bootstrap recovery is required, the existing production infrastructure deployment may reconcile multiple idempotent Azure resources rather than only the originally missing identity. This deliberately matches the manual production bootstrap path instead of maintaining a second partial infrastructure definition.
- The deployment runner temporarily opens its own public IPv4 address on the Azure SQL firewall and removes it in `finally`; cleanup failure is treated as an operational failure.
- The migration runner intentionally rejects `GO` batch separators. Migrations that require batch-scoped constructs must be redesigned or the runner deliberately extended first.
