# Workslip database migrations

Production schema and data migrations are applied explicitly during the protected backend deployment flow. The API runtime must never execute these files at startup.

## File contract

- Name migrations `YYYYMMDD_HHMM_slug.sql`.
- Migration IDs are append-only. Never rename, delete, reorder or edit a migration after it has been applied to production.
- Each file is one transaction-safe T-SQL batch. `GO` separators are rejected by the runner.
- The migration runner records the SHA-256 checksum in `dbo.WorkslipSchemaMigrations` and fails if an applied migration is later modified.
- Migrations execute in lexical filename order and under a database application lock.
- A failed migration is rolled back and blocks the API deployment.

## Data and destructive changes

Before a migration that deletes data, narrows a type, rewrites identifiers, changes ownership or performs a material backfill is approved for production, the owning PR/release evidence must state:

1. the production-data precondition/query that was reviewed;
2. the backup/restore expectation before execution;
3. the forward-fix or rollback strategy if the application release cannot proceed;
4. any expected lock duration or availability impact.

Do not implement rollback by automatically running a down migration. Production recovery is an explicit operator decision because application code and data semantics may already have moved forward.

## Local/static validation

Run:

```powershell
pwsh ./src/BE/infrastructure/run-database-migrations.ps1 -Environment prod -ValidateOnly
```

`-ValidateOnly` performs repository-level checks only. It does not authenticate to Azure or connect to SQL.

## Production execution

The backend deployment workflow authenticates first with the ordinary GitHub deployment identity only to discover the dedicated migration identity, then switches to that identity for migration execution. The migration identity has database DDL/data permissions and SQL-firewall management only for the target SQL server. The ordinary API runtime identity keeps normal data read/write access and must not retain `db_ddladmin`.
