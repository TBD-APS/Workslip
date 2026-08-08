# ADR 0006: Explicit database migrations use a deployment-only identity

**Status:** Accepted  
**Date:** 2026-08-08  
**Issue:** WOR-367

## Context

Workslip production API startup now verifies database connectivity and does not need to create or modify schema. The existing SQL provisioning still granted the API runtime identity `db_ddladmin`, leaving an unnecessary schema-management capability on the ordinary web workload.

Future tenant-integrity and branch work requires reviewed schema/data changes. Those changes must be auditable, serialized and completed before application code that depends on them is deployed.

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

## Operational rules

Migration files are forward-only release artifacts. They are not silently edited after production application. A checksum mismatch fails closed.

A destructive or material data-transforming migration must document its production-data preconditions, backup/restore expectation, expected locking/availability impact and recovery/forward-fix strategy in the owning PR/release evidence before execution.

Automatic down-migration is deliberately not part of production rollback. Application rollback is allowed only when the resulting schema remains compatible; otherwise recovery is an explicit database operation.

## Consequences

### Positive

- The API runtime identity no longer carries schema-management rights.
- Schema changes are visible in deployment logs and happen before incompatible application code is released.
- Concurrent production migration attempts are controlled.
- Future schema work such as WOR-160 and WOR-364 has one durable rollout mechanism instead of adding startup mutation.

### Trade-offs

- Production infrastructure must reconcile the dedicated migration identity before the first deployment that uses the migration step.
- The deployment runner temporarily opens its own public IPv4 address on the Azure SQL firewall and removes it in `finally`; cleanup failure is treated as an operational failure.
- The migration runner intentionally rejects `GO` batch separators. Migrations that require batch-scoped constructs must be redesigned or the runner deliberately extended first.
