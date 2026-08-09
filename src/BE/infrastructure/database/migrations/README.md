# Workslip database migrations

Production schema and data migrations are applied explicitly during the protected backend deployment flow. Production and staging API startup never execute these files.

Local development has one narrow exception: when the API runs in `Development` and `Azure:Sql:ConnectionString` resolves to a provably local SQL Server target, startup applies pending versioned migrations before the normal connectivity check. The same migration files, IDs and checksum history are reused; there is no separate development migration format.

Remote or ambiguous SQL targets are never auto-migrated. `Workslip:ApplyLocalMigrations=false` disables the local behavior. Setting `Workslip:ApplyLocalMigrations=true` is a strict assertion: startup fails if the target is not recognized as local rather than applying anything remotely.

## File contract

- Name migrations `YYYYMMDD_HHMM_slug.sql`.
- Migration IDs are append-only. Never rename, delete, reorder or edit a migration after it has been applied to production.
- Each file is one transaction-safe T-SQL batch. `GO` separators are rejected by the runner.
- The migration runner records the SHA-256 checksum in `dbo.WorkslipSchemaMigrations` and fails if an applied migration is later modified.
- Migrations execute in lexical filename order and under a database application lock.
- A failed migration is rolled back and blocks the relevant startup/deployment operation.

## Fresh local database bootstrap

On Windows, the default first-time developer path is:

```powershell
.\tools\dev\setup-local-db.cmd
```

The command targets SQL Server LocalDB only. It creates/starts the `MSSQLLocalDB` instance when needed and invokes the explicit Development-only `bootstrap-local-db` operation against `WorkslipLocal`.

Historical production migrations are not a supported from-zero schema definition. When the explicit bootstrap operation creates a brand-new empty local schema from the checked-out EF model, it records the currently known migration IDs/checksums in `dbo.WorkslipSchemaMigrations` with `AppliedBy=local-bootstrap` rather than replaying historical migration SQL against a schema that already represents the checked-out code. It then runs the DB-only synthetic development seed.

This baseline behavior is allowed only when EF `EnsureCreated` actually created the schema. An existing database is never re-baselined or recreated: bootstrap applies pending migrations normally and reconciles the idempotent development seed.

If a migration introduces a persistent database requirement that is not represented by the EF model, the owning change must also preserve fresh-local-bootstrap equivalence. Do not assume a historical migration will run after `EnsureCreated` on a new developer database.

## Local development execution

Supported local targets are deliberately narrow: `localhost`, IPv4/IPv6 loopback, `.`, `(local)`, SQL Server local instances such as `.\SQLEXPRESS`, and LocalDB. A machine name, Azure SQL hostname, LAN address or other remote/ambiguous target does not qualify.

With a local SQL connection configured, ordinary Development startup is enough:

```powershell
cd src/BE/WorkslipApi
dotnet run --launch-profile http
```

Startup applies only migrations missing from `dbo.WorkslipSchemaMigrations`. Re-running the same branch is idempotent. A checksum mismatch for an already-applied migration fails closed; the existing narrow LF/CRLF checksum reconciliation is preserved without rerunning migration SQL.

Development seeding remains a separate explicit opt-in through `Workslip:SeedDevelopmentData` after initial bootstrap. Auto-migration does not enable seeding or Entra provisioning.

## Data and destructive changes

Before a migration that deletes data, narrows a type, rewrites identifiers, changes ownership or performs a material backfill is approved for production, the owning PR/release evidence must state:

1. the production-data precondition/query that was reviewed;
2. the backup/restore expectation before execution;
3. the forward-fix or rollback strategy if the application release cannot proceed;
4. any expected lock duration or availability impact.

Do not implement rollback by automatically running a down migration. Production recovery is an explicit operator decision because application code and data semantics may already have moved forward.

## Static validation

Run:

```powershell
pwsh ./src/BE/infrastructure/run-database-migrations.ps1 -Environment prod -ValidateOnly
```

`-ValidateOnly` performs repository-level checks only. It does not authenticate to Azure or connect to SQL. The normal CI gate runs this validation for pull requests and `main`.

## Production execution

After green CI on `main`, the backend deployment authenticates first with the ordinary GitHub deployment identity only to discover the dedicated migration identity, then switches to that identity for migration execution. The migration identity has database DDL/data permissions and SQL-firewall management only for the target SQL server. The ordinary API runtime identity keeps normal data read/write access and must not retain `db_ddladmin`.
