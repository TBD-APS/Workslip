# Domain ownership and data integrity

- **Status:** Active
- **Owner:** Workslip maintainers
- **Source of truth:** `SqlDbContext`, `DatabaseSchemaInitializer` and executable persistence tests
- **Review cadence:** On tenant-boundary, database-lifecycle or deletion-flow changes

## Tenant ownership

`OrganizationId` is the database-owned tenant boundary for operational and master data. API filters remain required for authorization, but they are not a substitute for database constraints.

| Data | Tenant rule |
|---|---|
| Users and customers | Belong to exactly one organization. Their `(OrganizationId, Id)` alternate keys are the principal keys for tenant-scoped references. |
| Jobs | Belong to exactly one organization. A linked customer and the latest submission owner must have the same `OrganizationId`; customer snapshot columns remain independent value copies and may exist without `CustomerId`. The latest actor who moves a job to `InReview` is persisted as `SubmittedByUserId` and is the authoritative rejection recipient. |
| Worksheets | `OrganizationId`, `JobId` and `UserId` must resolve to one organization through composite foreign keys. |
| Job installations | The selected job and installation definition are tenant-scoped through composite foreign keys. |
| Installation category/control-point snapshots | The nested snapshot rows do not currently carry `OrganizationId`, so their category and control-point references cannot yet be tenant-enforced. This is tracked in [WOR-160](https://linear.app/workslip/issue/WOR-160/tenant-sikr-installationssnapshot-kategorier-og-kontrolpunkter). |
| Push subscriptions, notification queue and job views | `UserId` must resolve to an existing user. These tables do not currently duplicate `OrganizationId`; tenant authorization is enforced before their rows are written or queried. |

Job views use separate `ViewType` acknowledgements for the ordinary job view (`New`) and the post-approval view (`Completed`). An approved job is therefore unread for an assigned user until that user opens the approved report, even when the same user viewed the job while completing it.

Superadmins remain ordinary organization-bound user rows. The two canonical development Superadmins permanently belong to the deterministic internal `Workslip Platform` organization, which is not an operational customer tenant. Customer-organization administration queries exclude that organization by its reserved ID, and delegated-session lookup rejects the same ID. The reserved synthetic CVR is validated during development startup as an integrity marker; runtime queries do not filter by CVR, so a customer CVR collision remains visible and startup fails explicitly rather than silently hiding a tenant.

Cross-organization operational access is not represented by moving the row, making `OrganizationId` nullable, or creating duplicate memberships. Instead, `/superadmin` may issue a short-lived delegated token after verifying the actor's current database role and the target customer organization. The token preserves the real actor ID and `Superadmin` role while replacing only the effective `organizationId` claim. Existing tenant repositories therefore retain their normal organization filters without a special bypass.

Development startup preflights the complete reserved organization identity, canonical IDs and normalized emails, platform-tenant contamination, and all user-bound operational references before it changes local data or calls Microsoft Graph. Platform/demo creation and canonical user reconciliation run in one serializable relational transaction. Graph identities are resolved before canonical local writes; any newly created identities are tracked and deleted in reverse order if a later Graph call, concurrency-guarded user update, database write, or commit fails. The local transaction is rolled back on the same failure. Moving a canonical Superadmin therefore never silently moves tenant data or leaves a partially reconciled platform identity.

The frontend stores the original Superadmin token separately, clears tenant query state when entering or leaving a delegated session, and displays the active organization. The delegated token expires after 15 minutes by default and has no refresh flow. Superadmins cannot register tenant push subscriptions during this flow.

## Foreign-key deletion behavior

| Dependent relation | Delete behavior | Reason |
|---|---|---|
| `PushSubscriptions.UserId -> Users.Id` | Restrict / no action | A user cannot be removed while subscription delivery records still refer to their subscriptions. Cleanup must be explicit. |
| `NotificationQueue.UserId -> Users.Id` | Restrict / no action | Notification history and pending worker state must not disappear implicitly. |
| `JobViews.UserId -> Users.Id` | Restrict / no action | User deletion is explicit and must account for all user-owned job state. |
| `JobViews.JobId -> JobReports.Id` | Cascade | Views are disposable projections of a job and have no meaning after that job is removed. |
| `Worksheets -> Organizations, JobReports, Users` | Restrict / no action | Time registration is operational history. Jobs, users and organizations cannot be removed while it exists. |
| `JobReports -> Customers` | Restrict / no action | Customer deletion first clears the optional link in the repository; job snapshot values remain unchanged. |
| `JobReports.(OrganizationId, SubmittedByUserId) -> Users.(OrganizationId, Id)` | Restrict / no action | Rejection routing must retain the latest tenant-valid submission owner. Startup backfills this field from raw `InReview` audit events for legacy jobs; jobs without recoverable ownership remain nullable and use the bounded current-assignee fallback. |
| `JobReportInstallations -> JobReports` | Cascade | Installation selections are owned by the job. |
| `JobReportInstallations -> InstallationTypeDefinitions` | Restrict / no action | Referenced definitions cannot be removed while used by a job. |

## WOR-150 schema upgrade

`DatabaseSchemaInitializer` applies the integrity upgrade in one transaction and takes a SQL Server application lock so concurrent API starts cannot interleave this schema change.

Before any constraint is replaced or added, startup validates:

- orphaned user references in push subscriptions, queued notifications and job views;
- orphaned or cross-tenant worksheet job/user references;
- orphaned or cross-tenant job customer references.

If invalid rows exist, startup fails with the constraint name, relation and row count. No data is repaired or deleted automatically. Correct the reported rows, then restart the API to roll forward.

Rollback is schema-only: drop the six `WOR-150` foreign keys and recreate the former simple worksheet/customer foreign keys if an emergency rollback is required. Do not roll back by deleting rows. The composite constraints are intentionally stricter than the previous schema, so application rollback should only happen after confirming the older version will not create new cross-tenant references.
