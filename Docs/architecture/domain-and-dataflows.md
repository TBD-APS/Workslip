# Domain ownership and data integrity

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** `SqlDbContext`, domain models, versioned database migrations and executable persistence tests  
**Review cadence:** On tenant-boundary, persistence or lifecycle changes

This page records tenant/data-integrity boundaries that are expensive to reconstruct from individual entities. Exact columns, indexes and migration mechanics belong in the model/schema source.

## Tenant boundary

`OrganizationId` is the server-owned tenant boundary for Workslip operational data. API authorization and repository filters are still required; database constraints provide an additional integrity boundary and do not replace authorization.

Current tenant-scoped relationships include users, customers, jobs, assignments, worksheets and top-level job installation selections. Composite keys/foreign keys are used where a child must reference an entity from the same organization.

Superadmin access does not remove ordinary tenant filtering from repositories. Cross-organization operational work uses the explicit delegated-organization session flow so existing services continue to operate with one effective organization context.

## Known database-enforcement gap

Installation category/control-point snapshot rows remain a verified gap tracked by **WOR-160**:

- `JobReportInstallationCategoryRow` has no `OrganizationId`; its `ControlCategoryId` relationship is a simple foreign key.
- `JobReportInstallationControlPointRow` has no `OrganizationId`; its `ControlPointId` relationship is a simple foreign key.

The application can validate selections, but the database cannot currently prove that those two referenced definition rows belong to the same tenant as the parent installation. WOR-160 owns the relational fix and negative cross-tenant tests.

Do not describe installation snapshots as fully database tenant-enforced until that issue is implemented and verified.

## Lifecycle and deletion

Deletion behaviour should reflect ownership:

- disposable child state that has no meaning without its parent may cascade;
- operational/history/reference relationships that must not disappear implicitly use restrictive deletion and explicit cleanup;
- tenant-owned references must preserve organization scope during update/delete flows.

The exact current foreign keys and delete behaviours are defined in `SqlDbContext` plus applied versioned migrations. When changing them, validate existing production data before tightening constraints and use relational tests for orphan, cross-tenant and delete behaviour.

## Schema changes

Production schema/data changes are explicit deployment work under ADR 0006. Reviewed SQL migrations live in `src/BE/infrastructure/database/migrations`, are checksum-tracked and are applied by the protected backend deployment before the API package is deployed.

Normal production API startup only verifies database connectivity/readiness. It does not call EF migration APIs, schema initializers or custom migration SQL. Development-only seeding remains an explicit local-development concern and is not a production migration mechanism.

Do not copy one-off migration procedure details into this page after rollout. Keep durable integrity rules here and historical rollout details in the owning issue/PR.
