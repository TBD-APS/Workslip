---
title: 'Repair development login users in existing databases'
type: 'bugfix'
created: '2026-07-23'
status: 'done'
baseline_commit: '0522376add949633becb59e2fa760bcc8455639e'
context:
  - 'AGENTS.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The development-login buttons request users whose definitions exist in `DatabaseSeeder`, but the seeder returns as soon as it finds any organization. Local databases created before those role users were added therefore keep starting without them, and `/api/dev/token` returns `404 User not found`.

**Approach:** Make development seeding add absent canonical development identities on every development startup, including databases that already contain an organization. Keep the operation idempotent and retain the current full demo-data seed for empty databases.

## Boundaries & Constraints

**Always:** Preserve existing organizations, users, jobs, and assignments; use the canonical emails, stable IDs, display names, and roles already defined by the seed; associate missing development users with the deterministic existing seed organization; leave any pre-existing row that already owns a canonical ID or email unchanged; keep the repair limited to the development-only startup seed path; add regression tests for an existing database and repeat execution.

**Ask First:** Any solution that deletes or recreates the local database, changes existing non-development identities, introduces a migration, or alters production authentication behavior.

**Never:** Reset or overwrite user data, make the token endpoint create arbitrary users on request, weaken normal authorization, change Microsoft/passkey or one-time-code login, or modify the existing uncommitted `DatabaseSchemaInitializer.cs` work.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Existing local database | At least one organization exists, but one or more canonical development users have neither a matching ID nor email | Missing User, Auditor, Admin, and Superadmin identities are inserted into the selected existing organization and become resolvable by email | Existing data remains unchanged |
| Partially seeded database | Some canonical development users already exist | Only identities with neither a matching canonical ID nor email are inserted | No duplicate IDs or emails |
| Conflicting identity | An existing row already owns a canonical ID or email with different data | The existing row is preserved and no competing canonical row is inserted | Conflict repair is outside this change |
| Repeated startup | Reconciliation runs more than once | The second and later runs make no data changes | No duplicate users |
| Empty database | No organization exists | Existing complete demo-data seeding runs and creates the canonical development identities once | Existing seed behavior is preserved |

</frozen-after-approval>

## Code Map

- `src/BE/WorkslipApi/Program.cs` -- invokes `DatabaseSeeder.Seed` only when the ASP.NET Core environment is Development.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseSeeder.cs` -- defines the canonical development users but currently exits before adding them to an existing database.
- `src/BE/WorkslipApi/Endpoints/DevEndpoints.cs` -- resolves the requested email and returns `404` when the seed identity is absent.
- `src/FE/src/features/auth/routes/Login.tsx` -- supplies the four canonical development-login emails.
- `src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DatabaseSeederTests.cs` -- new regression coverage for reconciliation and idempotency.

## Tasks & Acceptance

**Execution:**

- [x] `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseSeeder.cs` -- extract the canonical identity definitions and reconcile them before the existing-organization early return, using one save only when users are missing.
- [x] `src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DatabaseSeederTests.cs` -- exercise an existing organization with a partial user set, assert all email/role mappings, run the seed again, and assert no duplicates.

**Acceptance Criteria:**

- Given an existing development database that has neither the canonical Admin ID nor `admin@17v3ygzs.mailosaur.net`, when the API starts, then the Admin development-login button receives a token instead of a not-found result.
- Given a canonical development-login identity has neither an existing canonical ID nor email, when development seeding runs, then that canonical email resolves to a user with the expected role.
- Given development seeding has already repaired the identities, when it runs again, then user count and identity records remain unchanged.
- Given a non-development environment, when the API starts, then this reconciliation is not invoked.

## Spec Change Log

- 2026-07-23 — Adversarial review found that repairing conflicting canonical IDs/emails would violate the preserve-existing-data boundary. The user explicitly chose to keep the earlier implementation. The frozen intent now defines reconciliation as insert-only and leaves identity conflicts unchanged. KEEP: centralized canonical definitions, deterministic organization selection, idempotent missing-user insertion, and focused regression coverage.

## Design Notes

The repair belongs in startup seeding rather than the token endpoint: the token endpoint should continue to authenticate known users only. Centralizing the four canonical definitions also prevents the empty-database and existing-database paths from drifting apart.

For an existing database, select the organization deterministically by oldest `CreatedAt`, then `Id`, matching the intent of maintaining one stable local demo tenant without depending on provider-specific row order.

## Verification

**Commands:**

- `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj --filter FullyQualifiedName~DatabaseSeederTests` -- expected: reconciliation and repeat-run tests pass.
- `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj` -- expected: backend regression suite passes.
- `dotnet build src/BE/WorkslipApi/Workslip.Api.csproj` -- expected: API and referenced projects build without errors.

**Manual checks:**

- Start the API in Development against the affected existing database, click each development-login button, and confirm it reaches the role-appropriate application route without a not-found banner.

## Suggested Review Order

**Development identity reconciliation**

- Central definitions keep empty and existing database paths aligned.
  [`DatabaseSeeder.cs:11`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseSeeder.cs#L11)

- Existing databases choose one deterministic tenant before reconciliation.
  [`DatabaseSeeder.cs:56`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseSeeder.cs#L56)

- Insert-only matching preserves existing ID or email conflicts unchanged.
  [`DatabaseSeeder.cs:443`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseSeeder.cs#L443)

**Regression boundaries**

- Partial seeding proves missing identities join the oldest organization.
  [`DatabaseSeederTests.cs:37`](../../../src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DatabaseSeederTests.cs#L37)

- Repeat execution proves the repair is idempotent.
  [`DatabaseSeederTests.cs:91`](../../../src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DatabaseSeederTests.cs#L91)

- Conflict tests preserve the user-approved insert-only behavior.
  [`DatabaseSeederTests.cs:114`](../../../src/BE/WorkslipApi/Workslip.Tests/Infrastructure/DatabaseSeederTests.cs#L114)
