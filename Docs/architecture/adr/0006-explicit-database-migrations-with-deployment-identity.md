# ADR 0006: Explicit database migrations use a deployment-only identity

**Status:** Accepted  
**Date:** 2026-08-08  
**Issue:** WOR-367

## Context

Workslip production API startup now verifies database connectivity and does not need to create or modify schema. The existing SQL provisioning still granted the API runtime identity `db_ddladmin`, leaving an unnecessary schema-management capability on the ordinary web workload.

Future tenant-integrity and branch work requires reviewed schema/data changes. Those changes must be auditable, serialized and completed before application code that depends on them is deployed.

The first rollout of this model exposed an operational gap: if the dedicated migration identity had not been reconciled before the backend deployment ran, the deployment failed closed as designed but production remained on the previous API binary. Frontend releases could therefore advance beyond the production API contract until an operator manually ran the infrastructure bootstrap.

## Decision

Production database migrations are an explicit deployment operation.

- Versioned T-SQL files live under `src/BE/infrastructure/database/migrations`.
- CI validates migration filenames and transaction/batch constraints before a change is eligible to merge to `main`.
- The protected backend deployment applies pending migrations before the API package is deployed. Migration failure stops the release.
- Applied migration IDs and SHA-256 checksums are recorded in `dbo.WorkslipSchemaMigrations`; an applied file is immutable.
- Migration execution uses a database application lock plus the existing production deployment concurrency gate.
- A dedicated user-assigned identity, `id-<company>-<environment>-migration`, owns production schema migration permissions.
- The migration identity receives only the database DDL/data roles required to perform reviewed migrations and SQL-server firewall management needed for the ephemeral GitHub runner connection.
- The ordinary API runtime identity retains normal database read/write access and must not be a member of `db_ddladmin`.
- The ordinary GitHub application-deployment identity may read the migration identity resource only to resolve its client ID; it does not inherit the migration identity's SQL permissions.
- API startup remains limited to connectivity/readiness checks plus explicitly enabled Development-only seeding.
- If a production backend deployment fails and the migration identity is genuinely absent, a separate protected recovery workflow may use the existing production infrastructure identity to run the authoritative migration-identity reconciler and then rerun only the failed backend deployment jobs.
- The recovery workflow must no-op when the migration identity already exists so unrelated backend deployment failures are not hidden, retried or turned into infrastructure mutations.

## Operational rules

Migration files are forward-only release artifacts. They are not silently edited after production application. A checksum mismatch fails closed.

A destructive or material data-transforming migration must document its production-data preconditions, backup/restore expectation, expected locking/availability impact and recovery/forward-fix strategy in the owning PR/release evidence before execution.

Automatic down-migration is deliberately not part of production rollback. Application rollback is allowed only when the resulting schema remains compatible; otherwise recovery is an explicit database operation.

The production infrastructure identity remains deployment-only. Normal API runtime never receives or depends on that credential. Recovery is allowed only inside the protected `prod` GitHub environment and reuses `reconcile-database-migration-identity.ps1` so role assignments and identity ownership still have one authoritative implementation.

## Consequences

### Positive

- The API runtime identity no longer carries schema-management rights.
- Schema changes are visible in deployment logs and happen before incompatible application code is released.
- Concurrent production migration attempts are controlled.
- Future schema work such as WOR-160 and WOR-364 has one durable rollout mechanism instead of adding startup mutation.
- A missed one-time migration-identity bootstrap no longer leaves production indefinitely on a stale API revision after later green `main` releases.
- Recovery remains narrow: it runs only after a failed backend deployment, mutates infrastructure only when the migration identity is absent, and reruns only that failed deployment.

### Trade-offs

- The protected recovery workflow needs access to the existing production infrastructure OIDC identity, but only for the missing-identity repair path; normal backend deployment and application runtime keep their narrower identities.
- The deployment runner temporarily opens its own public IPv4 address on the Azure SQL firewall and removes it in `finally`; cleanup failure is treated as an operational failure.
- The migration runner intentionally rejects `GO` batch separators. Migrations that require batch-scoped constructs must be redesigned or the runner deliberately extended first.
