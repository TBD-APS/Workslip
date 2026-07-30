---
title: 'Give development Superadmins a platform organization'
type: 'bugfix'
created: '2026-07-30'
status: 'done'
baseline_commit: 'c05c16c5036ee6d0b60b65273673fbeb18e85fc9'
context:
  - '{project-root}/Docs/architecture/domain-and-dataflows.md'
---

# Give development Superadmins a platform organization

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Canonical development Superadmins are currently seeded into the demo or oldest customer organization. Their permanent organization therefore looks like a customer tenant even though they are platform operators who should enter customer tenants only through delegated organization sessions.

**Approach:** Seed one deterministic internal `Workslip Platform` organization and permanently associate canonical development Superadmins with it. Keep ordinary development users in the demo/customer organization, migrate already-canonical Superadmin rows on repeated startup, and exclude the internal organization from customer-tenant selection.

## Boundaries & Constraints

**Always:** Use stable platform-organization identity data; keep seeding idempotent; preserve canonical Superadmin IDs, emails, Entra identities, and roles; move only canonical Superadmins; keep Admin/User/Auditor identities and all operational data in their existing customer organization; create or repair the platform organization before Superadmin reconciliation; detect tenant-bound references before moving an existing Superadmin; hide the platform organization from the Superadmin customer list and reject it as a delegated-session target.

**Ask First:** Any change requiring nullable user organization IDs, a general organization-type schema migration, or reassignment/deletion of non-canonical users or tenant data.

**Never:** Attach canonical Superadmins to the first/oldest customer organization; duplicate a canonical Superadmin to represent platform membership; use a random organization ID; treat the internal platform organization as an operational customer tenant.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Empty development database | No organizations or users | Create platform and demo organizations; Superadmins use platform ID; other dev users use demo ID | One atomic/idempotent seed result |
| Existing customer database | Customer organizations exist; platform organization does not | Create platform organization and move exact canonical Superadmins to it | Preserve every non-canonical row and tenant |
| Existing Superadmin has tenant data | Canonical Superadmin is referenced by tenant-bound operational rows | Do not create cross-tenant references or silently move operational data | Fail with an actionable startup error before Graph reconciliation |
| Repeated startup | Platform organization and canonical users already exist | No duplicates or data churn | Stable snapshots across repeated runs |
| Reserved identity conflict | Platform ID or reserved CVR belongs to incompatible data | Fail before partial Superadmin reconciliation | Clear startup exception identifying the conflict |
| Customer organization listing/session | Platform organization exists | Omit it from selectable customer organizations and reject direct delegated access | Return not-found/invalid target through existing result handling |

</frozen-after-approval>

## Code Map

- `src/BE/WorkslipApi/Workslip.Domain/PlatformOrganization.cs` -- shared deterministic internal organization identity.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DatabaseSeeder.cs` -- keeps demo/customer organization ownership and ordinary development identities.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DevelopmentDatabaseSeeder.cs` -- owns the platform organization and reconciles both canonical Superadmins against it.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfOrganizationRepository.cs` -- excludes the reserved organization from customer listing and delegated lookup.
- `src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DatabaseSeederTests.cs` -- covers empty, existing, repeated, and conflicting seed states.
- `src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DevelopmentDatabaseSeederTests.cs` -- verifies both canonical Superadmins remain platform-bound across Entra reconciliation.
- `src/BE/WorkslipApi/Workslip.Tests/Organizations/EfOrganizationAdministrationRepositoryTests.cs` -- verifies the internal organization cannot be selected as a customer tenant.
- `Docs/architecture/domain-and-dataflows.md` -- records the permanent platform home organization boundary.

## Tasks & Acceptance

**Execution:**
- [x] Add a shared reserved platform organization definition with deterministic ID, name, and synthetic eight-digit CVR.
- [x] Remove Rasmus from `DatabaseSeeder` so its demo/customer seed owns only Admin/User/Auditor identities and data.
- [x] Make `DevelopmentDatabaseSeeder` preflight the complete reserved organization and both canonical identities before mutation, rejecting non-canonical users, operational rows, normalized email conflicts, and tenant references.
- [x] Run platform/demo creation and all canonical user writes in one serializable database transaction; resolve both Graph identities before committing local changes and compensate every newly created Graph identity if later Graph or database work fails.
- [x] Reconcile canonical display name, phone, email, role, organization, and Entra fields without overwriting a concurrently changed row.
- [x] Filter the reserved organization from customer administration queries and delegated lookup by reserved ID; treat a reserved-CVR collision as an integrity failure rather than silently hiding a customer.
- [x] Update seed and repository tests for fresh, existing, repeated, conflict, contamination, compensation, and concurrency-safe states, including executable evidence for the relational move path.
- [x] Update the architecture documentation with the internal-versus-delegated organization distinction and transactional migration behavior.

**Acceptance Criteria:**
- Given a fresh development database, when startup seeding completes, then both canonical Superadmins belong to `Workslip Platform` and ordinary demo identities belong to the demo company.
- Given canonical Superadmins previously attached to a customer, when seeding runs, then only those exact canonical rows move to the platform organization.
- Given a canonical Superadmin with tenant-bound operational references, when seeding runs, then startup fails before moving the row or calling Graph.
- Given the exact reserved platform ID/CVR contains non-canonical users or operational rows, when seeding runs, then startup fails without renaming or hiding that tenant.
- Given an existing platform organization and canonical users, when seeding runs repeatedly, then organization/user counts and identity data remain stable.
- Given the organization administration API, when it lists or resolves customer tenants, then the platform organization is not exposed as a selectable delegated tenant.
- Given any canonical ID/email conflict, when seeding runs, then it fails without mutating unrelated tenant data or partially provisioning Entra identities.
- Given the second Graph reconciliation or any local write fails, when an earlier Graph identity was newly created, then local database changes roll back and every newly created identity is deleted.
- Given a customer happens to use the reserved CVR under another ID, when normal organization queries run, then the customer is not silently filtered; development startup reports the identity conflict.

## Spec Change Log

- **Iteration 1 — transactional and reserved-tenant safety:** Review found that the first implementation committed the platform organization, demo seed, and each Superadmin separately, and accepted an exact reserved ID/CVR row even when it contained customer data. The execution plan now requires a serializable local transaction, compensation of all newly created Graph identities, complete reserved-organization contamination checks, consistent trimmed-email identity resolution, full canonical-field reconciliation, concurrency predicates, ID-only runtime filtering, and relational-path evidence. This avoids partial startup state, cross-tenant races, accidental conversion of a customer tenant, and silent hiding of CVR collisions. **KEEP:** deterministic platform identity, separate customer/platform seed ownership, pre-Graph identity/reference validation, customer-list/session exclusion, focused boundary tests, and architecture documentation.

## Design Notes

The user table remains organization-bound, matching the existing authorization model. `DatabaseSeeder` continues to own customer/demo data, while `DevelopmentDatabaseSeeder` owns the internal organization and platform identities. A deterministic reserved organization supplies a permanent home claim for platform operators; delegated tokens remain the only mechanism that substitutes an operational customer organization.

Local seeding is one serializable transaction even though `DatabaseSeeder` performs intermediate `SaveChanges` calls; those writes remain uncommitted until the outer transaction succeeds. Graph ensures happen only after all local preflight checks. Newly created Graph identities are tracked as a set and compensated in reverse order if a later Graph call or any database operation fails. Existing Graph identities are idempotently reused.

The reserved ID is the runtime discriminator. The reserved CVR is validated during development seeding as a collision detector, but repository queries do not hide unrelated rows merely because their CVR matches. An existing exact platform identity is valid only when it contains no non-canonical users or customer/operational data; canonical Superadmins may be absent and repaired.

## Verification

**Commands:**
- `dotnet test .\Workslip.slnx --no-restore --filter "FullyQualifiedName~DatabaseSeederTests|FullyQualifiedName~DevelopmentDatabaseSeederTests|FullyQualifiedName~EfOrganizationAdministrationRepositoryTests|FullyQualifiedName~OrganizationSessionServiceTests"` -- expected: focused seed and organization boundary tests pass.
- `dotnet test .\Workslip.slnx --no-restore --filter "FullyQualifiedName~DevelopmentDatabaseSeederRelationalTests"` -- expected: relational move, rollback, and affected-row checks pass.
- `dotnet build .\Workslip.slnx --no-restore` -- expected: backend solution compiles.
- `git diff --check` -- expected: no whitespace errors or conflict markers.

## Suggested Review Order

**Transactional reconciliation**

- Start with the serializable transaction, Graph ordering, rollback, and reverse compensation.
  [`DevelopmentDatabaseSeeder.cs:37`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DevelopmentDatabaseSeeder.cs#L37)

- Review reserved identity, contamination, normalized-email, and tenant-reference preflight boundaries.
  [`DevelopmentDatabaseSeeder.cs:179`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DevelopmentDatabaseSeeder.cs#L179)

- Inspect affected-row predicates that reconcile every canonical field without overwriting concurrent changes.
  [`DevelopmentDatabaseSeeder.cs:449`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DevelopmentDatabaseSeeder.cs#L449)

**Tenant boundaries**

- Confirm customer seeding excludes the platform ID while preserving oldest-customer ownership.
  [`DatabaseSeeder.cs:53`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DatabaseSeeder.cs#L53)

- Verify customer listing and delegated lookup filter only the reserved platform ID.
  [`EfOrganizationRepository.cs:196`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfOrganizationRepository.cs#L196)

- Read the documented permanent-home versus delegated-tenant model and failure semantics.
  [`domain-and-dataflows.md:21`](../../architecture/domain-and-dataflows.md#L21)

**Executable evidence**

- Follow relational move, Graph-failure rollback, and concurrency affected-row tests.
  [`DevelopmentDatabaseSeederRelationalTests.cs:21`](../../../src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DevelopmentDatabaseSeederRelationalTests.cs#L21)

- Review fresh, repeated, conflict, reference, and contamination coverage.
  [`DevelopmentDatabaseSeederTests.cs:20`](../../../src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DevelopmentDatabaseSeederTests.cs#L20)

- Confirm reserved-CVR customers stay visible while reserved-ID lookup remains blocked.
  [`EfOrganizationAdministrationRepositoryTests.cs:16`](../../../src/BE/WorkslipApi/Workslip.Tests/Organizations/EfOrganizationAdministrationRepositoryTests.cs#L16)

**Reserved identity**

- Finish with the shared deterministic platform ID, display name, and synthetic CVR.
  [`PlatformOrganization.cs:3`](../../../src/BE/WorkslipApi/Workslip.Domain/PlatformOrganization.cs#L3)
