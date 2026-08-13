# Domain ownership and data integrity

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** `SqlDbContext`, domain models, versioned database migrations and executable persistence tests  
**Review cadence:** On tenant-boundary, persistence or lifecycle changes

This page records tenant/data-integrity boundaries that are expensive to reconstruct from individual entities. Exact columns, indexes and migration mechanics belong in the model/schema source.

## Tenant boundary

`OrganizationId` is the server-owned tenant boundary for Workslip operational data. API authorization and repository filters are still required; database constraints provide an additional integrity boundary and do not replace authorization.

Current tenant-scoped relationships include users, customers, jobs, assignments, worksheets and the selected-installation snapshot chain. Composite keys/foreign keys are used where a child must reference an entity from the same organization.

Superadmin access does not remove ordinary tenant filtering from repositories. Cross-organization operational work uses the explicit delegated-organization session flow so existing services continue to operate with one effective organization context.

## User audiences

`Role` and `UserKind` are separate concerns. `Role` controls authorization; `UserKind` identifies which user audience an identity belongs to.

Current audiences are:

- `Member` — normal customer identities and the default for existing users;
- `InternalTest` — internal QA identities that still exercise ordinary roles such as `User` or `Admin`.

Non-Superadmin user discovery, user management and job-assignment targeting are restricted to the authenticated actor's `UserKind` in addition to the Organization, Filial and role checks. Superadmin may administer both audiences through the cross-organization user-management flow. Authentication lookup is intentionally not filtered by `UserKind`, so an internal test identity can sign in with its real role.

Direct tenant user creation and invitations inherit the actor audience. Superadmin-created users default to `Member` unless explicitly classified as `InternalTest`. Pending invitations persist the audience so enrollment cannot accidentally move a test identity into the customer-visible user group.

Reclassifying an identity changes future discovery, management and assignment eligibility only. It does not repartition historical jobs, worksheets, assignments or audit data. See ADR 0008.

## Filial ownership

Workslips domain term is **Filial**. `Branch`, `BranchId`, `OrganizationBranch` and similar names are not Workslip domain terminology; Git branch remains ordinary version-control terminology.

A Filial is a child of an Organization, not a tenant boundary:

```text
Organization
  └── Filial
       ├── Users
       └── Jobs
```

Every Organization has one default Filial. `Users` and `JobReports` carry `FilialId`, while `OrganizationId` remains the security/tenant authority. Database relationships use `(OrganizationId, FilialId)` so an ID from another Organization cannot be attached as a Filial.

Job assignments are Filial-scoped. Tenant `User` employees and `Admin` users can be assignment targets when they belong to the same `OrganizationId` and `FilialId` as the Job. Admin and Superadmin roles may manage assignments; Auditor and Superadmin are not assignment targets. The UserKind audience rule above is an additional server-side assignment constraint.

Current single-filial flows resolve the default Filial server-side. Existing create-user/create-job contracts therefore do not require clients to send `FilialId`. Customers and installation/reference data remain Organization-level until a concrete product requirement says otherwise.

## Installation snapshot integrity

Selected installation snapshots carry `OrganizationId` through the database-owned hierarchy:

```text
JobReportInstallation
  -> JobReportInstallationCategory
      -> JobReportInstallationControlPoint
```

Category snapshots are constrained to the same Organization as both their parent installation and referenced `ControlCategory`. Control-point snapshots are constrained to the same Organization as both their parent category snapshot and referenced `ControlPoint`.

Application code derives snapshot ownership from the tenant-scoped parent and only loads allowed definitions through the effective Organization. Composite database foreign keys are the final integrity boundary for direct SQL/import/worker paths.

## Lifecycle and deletion

Deletion behaviour should reflect ownership:

- disposable child state that has no meaning without its parent may cascade;
- operational/history/reference relationships that must not disappear implicitly use restrictive deletion and explicit cleanup;
- tenant-owned references must preserve organization scope during update/delete flows.

The exact current foreign keys and delete behaviours are defined by the current model plus applied versioned migrations. When changing them, validate existing production data before tightening constraints and use relational tests for orphan, cross-tenant and delete behaviour.

## Schema changes

Production schema/data changes are explicit deployment work under ADR 0006. Reviewed SQL migrations live in `src/BE/infrastructure/database/migrations`, are checksum-tracked and are applied by the protected backend deployment before the API package is deployed.

Normal production API startup only verifies database connectivity/readiness. It does not call EF migration APIs, schema initializers or custom migration SQL. Development-only seeding remains an explicit local-development concern and is not a production migration mechanism.

Do not copy one-off migration procedure details into this page after rollout. Keep durable integrity rules here and historical rollout details in the owning issue/PR.
