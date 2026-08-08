# Domain ownership and data integrity

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** `SqlDbContext`, domain models, versioned database migrations and executable persistence tests  
**Review cadence:** On tenant-boundary, persistence or lifecycle changes

This page records tenant/data-integrity boundaries that are expensive to reconstruct from individual entities. Exact columns, indexes and migration mechanics belong in the model/schema source.

## Tenant boundary

`OrganizationId` is the server-owned tenant boundary for Workslip operational data. API authorization and repository filters are still required; database constraints provide an additional integrity boundary and do not replace authorization.

Current tenant-scoped relationships include users, customers, jobs, assignments, worksheets and the full selected-installation snapshot chain. Composite keys/foreign keys are used where a child must reference an entity from the same organization.

Superadmin access does not remove ordinary tenant filtering from repositories. Cross-organization operational work uses the explicit delegated-organization session flow so existing services continue to operate with one effective organization context.

## Installation snapshot integrity

Selected installation snapshots carry `OrganizationId` through each database-owned relationship:

```text
JobReport
  -> JobReportInstallation
      -> JobReportInstallationCategory
          -> JobReportInstallationControlPoint
```

The category snapshot has tenant-scoped composite foreign keys to both its parent installation and `ControlCategory`. The control-point snapshot has tenant-scoped composite foreign keys to both its parent category snapshot and `ControlPoint`.

This means a valid entity ID from another organization cannot be attached to the snapshot chain merely because the ID exists. Application validation still rejects invalid input earlier, while the database constraint remains the final data-integrity boundary for direct SQL/import/worker paths.

WOR-160 introduced this boundary and includes relational negative tests for cross-organization category/control-point references.

## Lifecycle and deletion

Deletion behaviour should reflect ownership:

- disposable child state that has no meaning without its parent may cascade;
- operational/history/reference relationships that must not disappear implicitly use restrictive deletion and explicit cleanup;
- tenant-owned references must preserve organization scope during update/delete flows.

The exact current foreign keys and delete behaviours are defined in `SqlDbContext` plus applied versioned migrations. When changing them, validate existing production data before tightening constraints and use relational tests for orphan, cross-tenant and delete behaviour.

## Schema changes

Production schema/data changes are explicit deployment work under ADR 0005. Reviewed SQL migrations live in `src/BE/infrastructure/database/migrations`, are checksum-tracked and are applied by the protected backend deployment before the API package is deployed.

Normal production API startup only verifies database connectivity/readiness. It does not call EF migration APIs, schema initializers or custom migration SQL. Development-only seeding remains an explicit local-development concern and is not a production migration mechanism.

Do not copy one-off migration procedure details into this page after rollout. Keep durable integrity rules here and historical rollout details in the owning issue/PR.
