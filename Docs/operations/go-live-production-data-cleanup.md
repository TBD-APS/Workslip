# Go-live production data cleanup

**Status:** Planned and implementation-ready; production execution not yet performed  
**Owner:** Workslip product owner / backend-infrastructure maintainer  
**Linear:** WOR-348, followed by WOR-351  
**Source of truth:** production Azure SQL state, current EF mappings, `src/BE/infrastructure/operations/cleanup-prelive-orders.sql`, `src/BE/infrastructure/operations/clear-selected-tables.sql`, executed command evidence, and production smoke evidence  
**Review cadence:** before the first customer go-live and whenever the cleanup scope changes

## Required end state

Before first customer access, production must contain:

- all existing `Users`, roles, organization memberships and Entra links;
- all approved existing `Customers`;
- all required installation reference data (`InstallationTypeDefinitions`, `ControlCategories`, `ControlPoints`, mappings);
- **zero `JobReports`** and no remaining job-dependent pre-live data.

The current product decision is that historical cases/orders are not imported for first go-live. Therefore all `JobReports` present before the first customer go-live are cleanup targets. This decision is recorded in WOR-348/WOR-334.

## Hard safety rules

- Do not run the destructive phase before PR #394 (WOR-321) is merged and deployed. Otherwise an API restart can still run development/demo seeding while production release testing is enabled.
- Do not delete or deactivate `Users`.
- Do not delete `Customers`, `Organizations`, installation definitions/categories/control points/mappings, push subscriptions or invitation identities as part of this cleanup.
- Do not run against an unverified database name.
- Do not execute from an old dry run. The destructive invocation must provide the exact current `JobReports` count observed immediately beforehand.
- Stop the API/background workers for the destructive maintenance window so no new case or notification can be created concurrently.
- Verify Azure SQL point-in-time recovery/rollback capability before mutation and record the recovery point/time in the restricted operational record. Do not put customer content, tokens or credentials in GitHub/Linear.
- If any count or post-condition differs from the expected state, stop. Do not weaken the script guards to make it continue.
- Do not use `Sql queries/DropTablesQuery.sql` for go-live data cleanup. It intentionally drops foreign keys and tables and is only a full schema-reset utility.

## Why the cleanup is explicit

Current relational behavior is mixed:

- `Worksheets` and `JobReportLinks` restrict deletion and must be removed explicitly;
- `JobAssignments`, `JobEvents`, `JobReportClosureFlags`, `JobReportInstallations` (including category/control-point snapshots), and `JobViews` cascade from `JobReports`;
- job notifications have no job foreign key; their `jobId` lives in the JSON payload and must be removed separately;
- idempotency records have no job foreign key; records that contain a target job GUID in scope/key/response are removed explicitly.

The cleanup is therefore a one-time controlled SQL operation, not startup behavior and not a permanent migration.

## Configurable whole-table cleanup helper

`src/BE/infrastructure/operations/clear-selected-tables.sql` exists for additional operator-approved cleanup where the **entire contents** of explicitly selected tables may be removed.

It does **not** replace `cleanup-prelive-orders.sql` as the canonical WOR-348 job cleanup. The job-specific script remains necessary because notifications and idempotency records can reference jobs without foreign keys, and it performs additional retained-identity/reference-data checks.

The configurable helper accepts a semicolon-separated allowlist:

```text
TablesToClear="dbo.TableA;dbo.TableB"
```

It fails closed when:

- the connected database name differs from `ExpectedDatabaseName`;
- a selected table does not exist;
- the list contains a protected go-live table (`Users`, `Customers`, `Organizations`, installation/reference data, push subscriptions, invitations, or migration history);
- a selected table is temporal or CDC-tracked;
- a selected table has an enabled `DELETE` trigger;
- a non-selected table has a foreign key into a selected table;
- the selected tables contain a cross-table foreign-key cycle;
- the table/count signature differs from the reviewed dry run;
- any selected table still contains rows after deletion.

The helper never drops or disables constraints. It calculates a child-before-parent delete order from SQL Server metadata, uses `DELETE`, executes inside one transaction, locks the selected tables before mutation, and outputs only table names/counts and a count signature.

Dry run example:

```powershell
$tables = "dbo.JobEvents;dbo.JobReports"

sqlcmd `
  -S "<production-sql-server>.database.windows.net" `
  -d "<production-database>" `
  -G -b -l 30 `
  -v ExpectedDatabaseName="<production-database>" TablesToClear="$tables" ExpectedCountSignature="DISCOVER" Execute="0" `
  -i ".\src\BE\infrastructure\operations\clear-selected-tables.sql"
```

Review the exact table list, counts and `CountSignature`. Destructive execution requires the same table set and the exact immediately preceding signature:

```powershell
sqlcmd `
  -S "<production-sql-server>.database.windows.net" `
  -d "<production-database>" `
  -G -b -l 30 `
  -v ExpectedDatabaseName="<production-database>" TablesToClear="$tables" ExpectedCountSignature="<dry-run-signature>" Execute="1" `
  -i ".\src\BE\infrastructure\operations\clear-selected-tables.sql"
```

If a logical reference is stored in JSON, text, a file, external storage or any other non-FK location, this generic helper cannot infer it. Use a purpose-built cleanup such as `cleanup-prelive-orders.sql`.

## Phase 0 — prerequisites

1. Merge and deploy PR #394 / WOR-321.
2. Verify the API starts without creating new development/demo rows.
3. Resolve/verify required existing customer import under WOR-334.
4. Confirm the exact production SQL database name from Azure resource state.
5. Verify point-in-time recovery is available for the database and record the rollback reference/time in the restricted operational record.
6. Ensure `sqlcmd` and an authorized Entra/Azure SQL login are available.

Production execution remains a destructive operation and requires explicit product-owner approval after the dry-run output has been reviewed.

## Phase 1 — dry run

Run with `Execute=0` and `ExpectedJobCount=-1`:

```powershell
sqlcmd `
  -S "<production-sql-server>.database.windows.net" `
  -d "<production-database>" `
  -G -b -l 30 `
  -v ExpectedDatabaseName="<production-database>" ExpectedJobCount="-1" Execute="0" `
  -i ".\src\BE\infrastructure\operations\cleanup-prelive-orders.sql"
```

The output contains counts only. It must not print case IDs, customer details, user emails, payload JSON or other personal data.

Record only these non-content facts in WOR-348/restricted release evidence:

- database/resource identity confirmation;
- timestamp;
- current `JobReports` count;
- counts of job-dependent rows;
- pre-cleanup counts for Users, Customers and installation reference tables;
- rollback/PITR verification reference.

If the dry run reports a state that is not understood, stop and inspect before mutation.

## Phase 2 — maintenance and execute

1. Stop the production API/App Service so HTTP requests and notification workers cannot create or mutate case state.
2. Re-run the dry run. Use this **immediately preceding** `JobReports` count as `ExpectedJobCount`.
3. Obtain explicit production-delete approval referencing that dry-run count and verified rollback point.
4. Execute:

```powershell
sqlcmd `
  -S "<production-sql-server>.database.windows.net" `
  -d "<production-database>" `
  -G -b -l 30 `
  -v ExpectedDatabaseName="<production-database>" ExpectedJobCount="<exact-dry-run-count>" Execute="1" `
  -i ".\src\BE\infrastructure\operations\cleanup-prelive-orders.sql"
```

The script acquires an exclusive `JobReports` lock, performs the cleanup in one transaction, and rolls back on any failed safety/post-condition.

## Phase 3 — immediate verification

Before reopening customer access:

1. Confirm the script reports `JobReportsAfter = 0`.
2. Confirm Users count equals the pre-cleanup count.
3. Confirm Customers count equals the pre-cleanup count, except for an explicitly approved WOR-334 import performed outside this cleanup window.
4. Confirm installation definition/category/control-point/mapping counts equal their pre-cleanup counts.
5. Start/restart the production API.
6. Re-run a read-only count check and confirm `JobReports = 0`; an API restart must not recreate demo cases.
7. Review startup/Application Insights logs for schema, EF, auth, reference-data or background-worker failures without copying personal data into public evidence.

If a restart recreates cases, stop go-live and roll back/escalate; do not repeatedly delete around an active seeder.

## Phase 4 — WOR-351 functionality gate

After cleanup, execute WOR-351 before go-live approval. At minimum it must verify:

- retained users can authenticate through normal live login paths;
- roles and tenant isolation remain correct;
- customer list/details load;
- create-case UI can load customers and installation reference data without persisting a production case;
- backend Release build and focused tests are green;
- frontend production build is green;
- production `public-smoke` is green;
- dev-only endpoints are unavailable after the live switch;
- no high/critical findings remain in seeding/cleanup/auth/tenant paths.

Full case mutation testing belongs in staging. If staging is unavailable and a production mutation test is explicitly approved in an isolated internal tenant, it must use synthetic data, be fully deleted afterward, and the final production state must again be `JobReports = 0`.

## Rollback

The SQL script rolls back automatically if an error occurs before commit. After commit, rollback uses the verified Azure SQL point-in-time recovery process recorded before execution. A restored database must be validated in isolation before any connection-string switch.

Do not overwrite the production database blindly from a restore. Preserve the failed/current database for investigation until the product owner approves the recovery path and any personal-data handling/retention implications are accounted for.

## Evidence classification

Until the destructive command and WOR-351 have actually run, report this work as:

- cleanup mechanism implemented;
- statically reviewed against current EF relationships;
- production cleanup **not executed**;
- production `JobReports = 0` **not yet verified**;
- post-cleanup functionality review **pending**.
