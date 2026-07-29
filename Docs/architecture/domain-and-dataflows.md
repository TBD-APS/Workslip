# Domain ownership and data integrity

- **Status:** Active
- **Owner:** Workslip maintainers
- **Source of truth:** `SqlDbContext`, `DatabaseSchemaInitializer`, authentication context and executable persistence tests
- **Review cadence:** On tenant-boundary, database-lifecycle, authorization-scope or deletion-flow changes

## Identity and organization scope

Workslip separates **platform identity** from **tenant operating context**.

| Identity | Persistent organization | Tenant context |
|---|---|---|
| `User`, `Auditor`, `Admin` | Exactly one `OrganizationId` | Derived only from the authenticated server-owned claim. |
| `Superadmin` | `OrganizationId = null` | Selected explicitly per session/request when entering an organization. |

A platform Superadmin is not a member of an arbitrary “home” organization. Its JWT and transformed Entra identity contain the Workslip user ID and `Superadmin` role but no permanent `organizationId` claim.

Ordinary tenant services still require `ICurrentUserContext.OrganizationId`. For a Superadmin, the client proposes that context through `X-Workslip-Organization-Id` after the backend has authenticated the caller as `Superadmin`. Middleware validates that the organization exists, caches successful validation briefly, and only then exposes it through `ICurrentUserContext`. Malformed or unknown values remain unscoped. For every other role, the header is ignored and the organization comes from the authenticated claim. This prevents a tenant user from overriding its tenant boundary.

Dedicated `/superadmin` application and repository paths are intentionally cross-tenant. They use explicit organization identifiers and must not be implemented by weakening ordinary tenant repositories or by globally bypassing organization filters.

The frontend stores one selected Superadmin organization scope and performs a full navigation when changing it so tenant-specific React Query state is not reused across organizations. This is defense in depth; backend validation and scoping remain the authorization boundary.

## Tenant ownership

`OrganizationId` is the database-owned tenant boundary for operational and master data. API filters remain required for authorization, but they are not a substitute for database constraints.

| Data | Tenant rule |
|---|---|
| Tenant users | `User`, `Auditor`, and `Admin` belong to exactly one organization. Their `(OrganizationId, Id)` unique key is the principal key for tenant-scoped references. |
| Platform users | `Superadmin` has `OrganizationId = null` and cannot be referenced by tenant worksheets or job assignments. |
| Customers | Belong to exactly one organization. Their `(OrganizationId, Id)` alternate key is the principal key for tenant-scoped references. |
| Jobs | Belong to exactly one organization. A linked customer must have the same `OrganizationId`; the customer snapshot columns are independent value copies and may exist without `CustomerId`. |
| Worksheets | `OrganizationId`, `JobId` and `UserId` must resolve to one organization through composite foreign keys. Platform Superadmins cannot own worksheet entries. |
| Job assignments | The assigned user and report must belong to the assignment organization. Platform Superadmins are operators, not tenant assignees. |
| Job installations | The selected job and installation definition are tenant-scoped through composite foreign keys. |
| Installation category/control-point snapshots | The nested snapshot rows do not currently carry `OrganizationId`, so their category and control-point references cannot yet be tenant-enforced. This is tracked in [WOR-160](https://linear.app/workslip/issue/WOR-160/tenant-sikr-installationssnapshot-kategorier-og-kontrolpunkter). |
| Push subscriptions, notification queue and job views | `UserId` must resolve to an existing user. These tables do not currently duplicate `OrganizationId`; tenant authorization is enforced before their rows are written or queried. Platform Superadmins do not register tenant push subscriptions or open the tenant notification stream. |

The database check constraint `CK_Users_RoleOrganizationScope` enforces the role/organization invariant:

```text
Superadmin  => OrganizationId IS NULL
other roles => OrganizationId IS NOT NULL
```

Tenant user-management validators also reject creation or promotion to `Superadmin`; platform identities must be provisioned through the controlled Superadmin identity path. Tenant CRUD also protects existing platform Superadmins from direct lookup, role changes and deletion.

## Platform-scope schema transition

`DatabaseSchemaInitializer` converts existing Superadmin rows to platform identities by making `Users.OrganizationId` nullable and setting the organization to `NULL` for every `Superadmin`.

Before detaching a Superadmin, startup verifies that no worksheet or job-assignment row references that account. Such references represent architectural drift and cause startup to fail rather than silently rewriting operational history. Resolve the reported references and restart to roll forward.

Development defers the role/organization check constraint until `DevelopmentDatabaseSeeder` has reconciled the canonical Rasmus and Mahad Superadmin rows through the existing Entra service. Production applies the constraint during schema initialization.

## Foreign-key deletion behavior

| Dependent relation | Delete behavior | Reason |
|---|---|---|
| `PushSubscriptions.UserId -> Users.Id` | Restrict / no action | A user cannot be removed while subscription delivery records still refer to their subscriptions. Cleanup must be explicit. |
| `NotificationQueue.UserId -> Users.Id` | Restrict / no action | Notification history and pending worker state must not disappear implicitly. |
| `JobViews.UserId -> Users.Id` | Restrict / no action | User deletion is explicit and must account for all user-owned job state. |
| `JobViews.JobId -> JobReports.Id` | Cascade | Views are disposable projections of a job and have no meaning after that job is removed. |
| `Worksheets -> Organizations, JobReports, Users` | Restrict / no action | Time registration is operational history. Jobs, tenant users and organizations cannot be removed while it exists. |
| `JobReports -> Customers` | Restrict / no action | Customer deletion first clears the optional link in the repository; job snapshot values remain unchanged. |
| `JobReportInstallations -> JobReports` | Cascade | Installation selections are owned by the job. |
| `JobReportInstallations -> InstallationTypeDefinitions` | Restrict / no action | Referenced definitions cannot be removed while used by a job. |

## WOR-150 schema upgrade

`DatabaseSchemaInitializer` applies the integrity upgrade in one transaction and takes a SQL Server application lock so concurrent API starts cannot interleave this schema change.

Before any constraint is replaced or added, startup validates:

- orphaned user references in push subscriptions, queued notifications and job views;
- orphaned or cross-tenant worksheet job/user references;
- orphaned or cross-tenant job customer references.

If invalid rows exist, startup fails with the constraint name, relation and row count. No operational history is repaired or deleted automatically. Correct the reported rows, then restart the API to roll forward.

Rollback is schema-only: drop the affected strict foreign keys/check constraint and recreate the former simple worksheet/customer foreign keys only after confirming the older application version will not create new cross-tenant references. Do not roll back by deleting rows.
