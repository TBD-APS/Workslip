---
title: 'WOR-321: Separate production installation baseline provisioning'
type: 'refactor'
created: '2026-08-08'
status: 'done'
baseline_commit: 'f558d9bc94aa9bf86cf59ed031060fb2ebc6d724'
context:
  - 'Docs/agents/VALIDATION.md'
  - 'Docs/compliance/GDPR_AI_ACT_BASELINE.md'
  - 'src/BE/WorkslipApi/AGENTS.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Explicit organization onboarding currently calls `InstallationSeeder` with `jobReports: []`. That class mixes required tenant reference-data creation with Bogus/AutoBogus-based demo JobReport installation snapshots, so production onboarding depends on a development-oriented seeding boundary.

**Approach:** Introduce a concrete, scoped `InstallationBaselineProvisioner` that only stages categories, control points, installation definitions, and mappings from `Data.json`. Inject it into organization onboarding, and keep all randomized JobReport installation snapshot creation behind development database seeding while preserving the existing single-save onboarding boundary.

## Boundaries & Constraints

**Always:** Work on the existing `rbj--321-stop-production-dev-seeding` branch and draft PR #394; provision baseline only during explicit create-organization onboarding or development seeding; keep production provisioning free of Bogus/AutoBogus and JobReport snapshot creation; propagate cancellation; stage organization, admin, and baseline for one `SaveChangesAsync`; preserve Development-only startup seeding and verification-only staging/production startup; keep every created row tenant-consistent.

**Ask First:** Any schema or migration change, reconciliation/idempotent backfill of existing tenants, change to `Data.json` content, new application-layer abstraction, or broader development seed behavior change.

**Never:** Resolve or invoke the provisioner from application startup; inspect or repair existing tenant baseline data; call the mixed `InstallationSeeder` from production code; create JobReports or `JobReportInstallation*` snapshots during onboarding; include unrelated branch/worktree changes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| New organization | Valid create request | Organization, admin, and all four baseline families commit together | Existing duplicate/update handling remains authoritative |
| Production-safe provision | Organization ID and valid `Data.json` | Only tenant reference rows are staged | Cancellation/file/validation failure prevents onboarding save |
| Development seed | Empty development database | Baseline plus demo jobs and randomized installation snapshots are created | Existing development transaction/compensation behavior remains intact |
| Non-development startup | Existing incomplete tenant | Connectivity is verified without provisioning or reconciliation | Startup fails only for existing readiness/connectivity failures |

</frozen-after-approval>

## Code Map

- `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/InstallationBaselineProvisioner.cs` -- production-safe baseline construction and staging from `Data.json`.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DevelopmentInstallationSnapshotSeeder.cs` -- randomized development-only JobReport installation snapshot construction.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DatabaseSeeder.cs` -- development-only demo graph creation and installation snapshot caller.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DevelopmentDatabaseSeeder.cs` -- scoped development seeding transaction and dependency entry point.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfOrganizationRepository.cs` -- explicit production onboarding and single-save boundary.
- `src/BE/WorkslipApi/Workslip.Infrastructure/DependencyInjection.cs` -- scoped production provisioner registration.
- `src/BE/WorkslipApi/Workslip.Tests/Organizations/EfOrganizationRepositoryOnboardingTests.cs` -- onboarding baseline and zero-order contract.
- `src/BE/WorkslipApi/Workslip.Tests/Infrastructure/` -- focused provisioner, development seed, startup, and DI coverage.
- `src/BE/WorkslipApi/README.md` -- maintained persistence/startup behavior.

## Tasks & Acceptance

**Execution:**
- [x] Split baseline construction/loading from randomized development snapshot construction, using explicit row initialization in the production-safe provisioner.
- [x] Inject and register the provisioner; replace `InstallationSeeder.Seed(..., jobReports: [])` in onboarding without adding an internal save.
- [x] Adapt development seeding to consume the same newly staged baseline and retain fake installation snapshots.
- [x] Add focused regression coverage for baseline integrity, zero onboarding snapshots, retained development snapshots, non-development non-mutation, and DI lifetime/resolution.
- [x] Correct the maintained backend startup description and prepare the validated local change for PR #394.

**Acceptance Criteria:**
- Given a valid new organization request, when onboarding completes, then categories, control points, definitions, and mappings exist for that tenant and all JobReport/snapshot tables remain empty.
- Given development startup on an empty database, when development seeding completes, then demo JobReports retain installation/category/control-point snapshots built from the provisioned baseline.
- Given staging or production startup with an incomplete existing tenant, when database readiness runs, then no tenant or reference row is added, changed, or reconciled.
- Given dependency injection constructs organization onboarding, when the scoped repository is resolved, then it receives the production-safe provisioner and no production path depends on the demo seeder.
- Given any provisioning failure before onboarding save, when the operation exits, then no partial organization/admin/baseline commit occurs.

## Spec Change Log

## Design Notes

Return an infrastructure-local representation of the newly staged definitions and mappings from the provisioner so development snapshot generation uses the exact same object graph. The provisioner is create-only: it performs no existence query, update, save, or reconciliation. A separate application interface is unnecessary because both consumer and behavior are infrastructure persistence concerns.

## Verification

**Commands:**
- `dotnet build Workslip.slnx --configuration Release --nologo` -- expected: backend restores and builds cleanly.
- `dotnet test Workslip.Tests/Workslip.Tests.csproj --configuration Release --no-build --no-restore --nologo --filter "FullyQualifiedName~EfOrganizationRepositoryOnboardingTests|FullyQualifiedName~InstallationBaselineProvisionerTests|FullyQualifiedName~DatabaseSeederTests|FullyQualifiedName~DevelopmentDatabaseSeederTests|FullyQualifiedName~DatabaseStartupTests"` -- expected: focused production/development boundaries pass.
- `dotnet test Workslip.slnx --configuration Release --no-build --no-restore --nologo` -- expected: full backend suite result is recorded, including unrelated baseline failures.
- `python tools/docs/check_docs.py` -- expected: maintained documentation checks pass.

## Implementation Evidence

- Production onboarding now depends on a scoped, create-only baseline provisioner with no Bogus/AutoBogus or JobReport snapshot behavior.
- Development seeding consumes the provisioned object graph and retains randomized installation/category/control-point snapshots.
- Release build passed with 0 warnings and 0 errors; 27 focused tests passed, including relational rollback.
- Full backend suite recorded 340 passing and 15 pre-existing failures in untouched SQLite/auth/audit suites.
- Documentation truth check passed for 29 maintained files.
- Three independent reviews found no unmet acceptance criterion. Their actionable cancellation, mapping assertion, and relational rollback gaps were patched; pre-existing seed-file validation hardening was deferred.
- Compliance assessment: no new personal-data processing or AI behavior; the change reduces accidental production mutation risk.

## Suggested Review Order

**Production onboarding boundary**

- Start where explicit onboarding stages the production-safe baseline before its single save.
  [`EfOrganizationRepository.cs:142`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfOrganizationRepository.cs#L142)

- Inspect explicit reference-row construction without fake-data dependencies or internal persistence.
  [`InstallationBaselineProvisioner.cs:8`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/InstallationBaselineProvisioner.cs#L8)

- Confirm scoped lifetime follows the scoped `SqlDbContext` and repository.
  [`DependencyInjection.cs:67`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/DependencyInjection.cs#L67)

**Development-only snapshots**

- See development seeding reuse the exact newly staged baseline graph.
  [`DatabaseSeeder.cs:355`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DatabaseSeeder.cs#L355)

- Verify all randomized JobReport snapshot logic remains isolated behind development seeding.
  [`DevelopmentInstallationSnapshotSeeder.cs:8`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DevelopmentInstallationSnapshotSeeder.cs#L8)

**Regression evidence**

- Relational failure proves organization, admin, and baseline roll back together.
  [`EfOrganizationRepositoryOnboardingTests.cs:57`](../../../src/BE/WorkslipApi/Workslip.Tests/Organizations/EfOrganizationRepositoryOnboardingTests.cs#L57)

- Provisioner coverage proves staged mappings, tenant consistency, and zero snapshots.
  [`InstallationBaselineProvisionerTests.cs:18`](../../../src/BE/WorkslipApi/Workslip.Tests/Infrastructure/InstallationBaselineProvisionerTests.cs#L18)

- Development coverage proves demo JobReports retain all installation snapshot families.
  [`DatabaseSeederTests.cs:32`](../../../src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DatabaseSeederTests.cs#L32)

- Startup coverage proves incomplete tenants remain untouched outside development.
  [`DatabaseStartupTests.cs:44`](../../../src/BE/WorkslipApi/Workslip.Tests/Configuration/DatabaseStartupTests.cs#L44)

**Maintained documentation**

- Persistence guidance now states verification-only production startup and explicit onboarding provisioning.
  [`README.md:72`](../../../src/BE/WorkslipApi/README.md#L72)
