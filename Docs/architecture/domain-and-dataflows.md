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

Job assignments are Filial-scoped. Tenant `User` employees can be assignment targets when they belong to the same `OrganizationId` and `FilialId` as the Job. An `Admin` may also assign a Job to themselves when they belong to that same Filial, but may not assign the Job to another Admin. Admin and Superadmin roles may manage assignments; Superadmin is not an assignment target.

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
