# Go-live production data cleanup

**Status:** Planned and implementation-ready; production execution not yet performed  
**Owner:** Workslip product owner / backend-infrastructure maintainer  
**Linear:** WOR-348, followed by WOR-351  
**Source of truth:** production Azure SQL state, current EF mappings, `src/BE/infrastructure/operations/cleanup-prelive-orders.sql`, `src/BE/infrastructure/operations/run-go-live-prelive-cleanup.ps1`, executed command evidence, and production smoke evidence  
**Review cadence:** before the first customer go-live and whenever the cleanup scope changes

## Required end state

Before first customer access, production must contain:

- all existing `Organizations` and organizational configuration;
- all `OrganizationFilials` after the WOR-385 filial migration is deployed;
- all existing `Users`, roles, organization/filial memberships and Entra links;
- all approved existing `Customers`, including their persisted customer-level settings such as favorite state;
- all required installation/reference data (`InstallationTypeDefinitions`, `ControlCategories`, `ControlPoints`, mappings);
- reference lookups such as `JobWorkKinds` and `JobClosureFlags`;
- identity/onboarding/device state not tied to deleted jobs, including `PushSubscriptions` and `InviteTokens`;
- database migration history, including `WorkslipSchemaMigrations` and any existing EF migration history;
- non-job notification/idempotency state;
- **zero `JobReports`** and no remaining job-dependent pre-live data.

The current product decision is that historical cases/orders are not imported for first go-live. Therefore all `JobReports` present before the first customer go-live are cleanup targets. This decision is recorded in WOR-348/WOR-334.

## Curated data policy

The first go-live cleanup is intentionally narrow. Do not choose tables ad hoc.

### Preserve completely

- `Organizations`
- `OrganizationFilials` when deployed
- `Users`
- `Customers`
- `InstallationTypeDefinitions`
- `InstallationTypeDefinitionMappings`
- `ControlCategories`
- `ControlPoints`
- `JobWorkKinds`
- `JobClosureFlags`
- `PushSubscriptions`
- `InviteTokens`
- `WorkslipSchemaMigrations`
- `__EFMigrationsHistory` if present

`InviteTokens` are preserved by WOR-348 because invitation cleanup is a separate identity/onboarding decision. Expired, revoked or unwanted pre-live invites may be reviewed and cleaned separately; do not mix that decision into case cleanup.

### Remove pre-live job/case state

- `JobReports`
- `Worksheets`
- `JobAssignments`
- `JobReportLinks`
- `JobEvents`
- `JobReportClosureFlags`
- `JobReportInstallations`
- `JobReportInstallationCategories`
- `JobReportInstallationControlPoints`
- `JobViews`

### Remove only job-linked rows

Do **not** clear these tables wholesale:

- `NotificationQueue`
- `NotificationDeliveryLog`
- `IdempotencyRecords`

Only rows that reference the deleted `JobReports` are removed. Other system state remains intact.

## Hard safety rules

- Do not run the destructive phase before PR #394 (WOR-321) is merged and deployed. Otherwise an API restart can still run development/demo seeding while production release testing is enabled.
- Do not delete or deactivate `Users`.
- Do not delete `Customers`, `Organizations`, `OrganizationFilials`, reference/master data, migration history, push subscriptions or invitation identities as part of this cleanup.
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

## Operator entry point

Use the curated PowerShell wrapper rather than selecting tables manually:

```text
src/BE/infrastructure/operations/run-go-live-prelive-cleanup.ps1
```

The wrapper prints the intended `KEEP`, `CLEAR` and `PARTIAL` groups before invoking the canonical SQL cleanup.

## Phase 0 — prerequisites

1. Merge and deploy PR #394 / WOR-321.
2. If WOR-385 has landed, verify the filial migration completed and `OrganizationFilials` are present.
3. Verify the API starts without creating new development/demo rows.
4. Resolve/verify required existing customer import under WOR-334.
5. Confirm the exact production SQL database name from Azure resource state.
6. Verify point-in-time recovery is available for the database and record the rollback reference/time in the restricted operational record.
7. Ensure `sqlcmd` and an authorized Entra/Azure SQL login are available.

Production execution remains a destructive operation and requires explicit product-owner approval after the dry-run output has been reviewed.

## Phase 1 — dry run

From the repository root:

```powershell
pwsh ./src/BE/infrastructure/operations/run-go-live-prelive-cleanup.ps1 `
  -Server "<production-sql-server>.database.windows.net" `
  -Database "<production-database>"
```

Dry-run is the default. No rows are changed.

The SQL output contains counts only. It must not print case IDs, customer details, user emails, payload JSON or other personal data.

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
2. Re-run the dry run.
3. Use the **immediately preceding** `JobReports` count as `ExpectedJobCount`.
4. Obtain explicit production-delete approval referencing that count and verified rollback point.
5. Execute:

```powershell
pwsh ./src/BE/infrastructure/operations/run-go-live-prelive-cleanup.ps1 `
  -Server "<production-sql-server>.database.windows.net" `
  -Database "<production-database>" `
  -Execute `
  -ExpectedJobCount <exact-dry-run-count>
```

The canonical SQL cleanup acquires an exclusive `JobReports` lock, performs the cleanup in one transaction, and rolls back on any failed safety/post-condition.

## Phase 3 — immediate verification

Before reopening customer access:

1. Confirm the script reports `JobReportsAfter = 0`.
2. Confirm Users count equals the pre-cleanup count.
3. Confirm Customers count equals the pre-cleanup count, except for an explicitly approved WOR-334 import performed outside this cleanup window.
4. Confirm Organizations and, when deployed, OrganizationFilials remain intact.
5. Confirm installation definition/category/control-point/mapping counts equal their pre-cleanup counts.
6. Confirm `WorkslipSchemaMigrations` remains intact so deployment migration state is preserved.
7. Start/restart the production API.
8. Re-run a read-only count check and confirm `JobReports = 0`; an API restart must not recreate demo cases.
9. Review startup/Application Insights logs for schema, EF, auth, reference-data or background-worker failures without copying personal data into public evidence.

If a restart recreates cases, stop go-live and roll back/escalate; do not repeatedly delete around an active seeder.

## Separate cleanup candidates

These may deserve cleanup before go-live, but they are **not** part of WOR-348 and should be reviewed separately:

- expired/revoked/obsolete `InviteTokens`;
- stale push subscriptions that are known to be invalid;
- unrelated failed/expired notification queue items according to the normal notification retention policy;
- expired idempotency records according to the normal idempotency retention policy.

Keeping these decisions separate prevents a case cleanup from accidentally becoming an identity, device or operational-history purge.

## Phase 4 — WOR-351 functionality gate

After cleanup, execute WOR-351 before go-live approval. At minimum it must verify:

- retained users can authenticate through normal live login paths;
- roles and tenant isolation remain correct;
- filial ownership remains correct when the filial rollout is deployed;
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
