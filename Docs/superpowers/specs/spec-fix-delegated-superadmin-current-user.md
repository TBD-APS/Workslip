---
title: 'Fix delegated Superadmin current-user resolution'
type: 'bugfix'
created: '2026-07-30'
status: 'done'
baseline_commit: 'ef97604bacd38986d3b620bf3b68d5d91e3af3d6'
context:
  - '{project-root}/Docs/api/contract.md'
  - '{project-root}/Docs/superpowers/specs/spec-default-superadmin-organization.md'
---

# Fix delegated Superadmin current-user resolution

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A delegated Superadmin token correctly scopes `organizationId` to the selected customer, but the real Superadmin row now belongs to `Workslip Platform`. `EfUserRepository.GetByIdAsync` requires those organizations to match, so `/api/auth/me` cannot load the signed actor and returns 500 immediately after entering NP Teknik.

**Approach:** Permit the authenticated actor to read only their own user row independently of the effective organization claim. Preserve normal tenant scoping for every other user lookup, then continue projecting the delegated customer ID in the `/api/auth/me` response.

## Boundaries & Constraints

**Always:** Trust only the authenticated `UserId` claim for the self-read exception; return the canonical database profile and role; report the delegated token's effective organization in the current-user response; preserve existing tenant filters for other IDs and all writes; cover the production EF repository rather than relying only on a permissive fake.

**Ask First:** Any change to JWT claim shape, delegated-session issuance, user organization ownership, profile-update behavior during delegation, or the global exception/error contract.

**Never:** Remove organization scoping from arbitrary user reads; use `homeOrganizationId` as the operational tenant; duplicate or temporarily move the Superadmin row into a customer; allow cross-tenant update/delete; special-case canonical development IDs.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Delegated `/api/auth/me` | Signed actor ID belongs to platform; effective organization is NP Teknik | Load the actor's platform row and return their profile with NP Teknik as `organizationId` | No not-found/500 |
| Cross-tenant user lookup | Requested ID is neither in the effective organization nor the signed actor | Return no user | Preserve tenant isolation |
| Normal tenant lookup | Requested user belongs to the effective organization | Return the user as before | No behavior change |
| Missing signed actor | Authenticated actor ID has no database row | Preserve current unauthorized failure behavior | Global status-code mapping is outside this fix |

</frozen-after-approval>

## Code Map

- `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfUserRepository.cs` -- currently combines user ID and effective-organization predicates.
- `src/BE/WorkslipApi/Workslip.Application/Users/IUserRepository.cs` -- needs an explicit authenticated-actor lookup contract separate from tenant reads.
- `src/BE/WorkslipApi/Workslip.Application/Auth/AuthService.cs` -- loads the signed actor and projects effective organization for Superadmins.
- `src/BE/WorkslipApi/Workslip.Application/Organizations/OrganizationSessionService.cs` -- verifies the authenticated actor before delegated token issuance.
- `src/BE/WorkslipApi/Workslip.Tests/Auth/AuthServiceEntraLoginTests.cs` -- has delegated response coverage whose fake repository masks the production failure.
- `src/BE/WorkslipApi/Workslip.Tests/Organizations/OrganizationSessionServiceTests.cs` -- verifies actor revalidation and repository-call boundaries.

## Tasks & Acceptance

**Execution:**

- [x] `src/BE/WorkslipApi/Workslip.Application/Users/IUserRepository.cs` and `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfUserRepository.cs` -- add a dedicated authenticated-actor lookup that first requires the requested ID to equal the signed actor, then reads that exact row without changing tenant-scoped `GetByIdAsync`, `UpdateAsync`, or `DeleteAsync`.
- [x] `src/BE/WorkslipApi/Workslip.Application/Auth/AuthService.cs` and `src/BE/WorkslipApi/Workslip.Application/Organizations/OrganizationSessionService.cs` -- use the dedicated lookup only for signed-actor identity reads; keep delegated profile mutations and tenant user operations on scoped `GetByIdAsync`.
- [x] `src/BE/WorkslipApi/Workslip.Tests/Auth/AuthServiceEntraLoginTests.cs` and `src/BE/WorkslipApi/Workslip.Tests/Organizations/OrganizationSessionServiceTests.cs` -- exercise the real EF actor lookup for platform-home/customer-effective `/me`, reject a different actor ID, and prove delegated update/delete read paths do not inherit the exception.

**Acceptance Criteria:**

- Given Rasmus belongs to `Workslip Platform` and holds an NP Teknik delegated token, when `/api/auth/me` runs, then it returns Rasmus with `organizationId` equal to NP Teknik.
- Given the same delegated context and another user outside NP Teknik, when that user's ID is queried, then the repository returns no row.
- Given an ordinary tenant-scoped lookup, when the user belongs to the effective organization, then existing behavior remains unchanged.

## Spec Change Log

- **Iteration 1 — isolate actor reads from tenant operations:** Review found that placing the self-read exception in general `GetByIdAsync` also let delegated profile/user update and delete services pass their read checks, after which tenant-scoped writes silently did nothing while returning success. The plan now requires a dedicated authenticated-actor lookup used only by `/api/auth/me` and delegated-session actor verification. This avoids false-success writes and preserves the existing meaning of tenant-scoped `GetByIdAsync`. **KEEP:** signed-actor equality guard, effective-organization projection, real EF repository coverage, cross-tenant non-self denial, unchanged JWT claims, and unchanged mutation predicates.

## Design Notes

The self-read exception is a separate repository operation keyed by equality between the requested ID and the signed `ICurrentUserContext.UserId`. General `GetByIdAsync`, `UpdateAsync`, and `DeleteAsync` remain organization-scoped, so tenant user/profile mutation services cannot accidentally inherit actor-home access.

The dedicated lookup supplies identity, profile, and current database role only to actor-verification flows. The delegated token remains the source of operational tenant scope, and `AuthService.ApplyEffectiveOrganization` supplies the selected customer ID in the response.

## Verification

**Commands:**

- `dotnet test .\src\BE\WorkslipApi\Workslip.slnx --no-restore --filter "FullyQualifiedName~AuthServiceEntraLoginTests"` -- expected: delegated real-repository and existing login tests pass.
- `dotnet test .\src\BE\WorkslipApi\Workslip.slnx --no-restore --filter "FullyQualifiedName~OrganizationSessionServiceTests|FullyQualifiedName~DelegatedOrganizationTokenTests"` -- expected: session issuance and claim tests pass.
- `dotnet build .\src\BE\WorkslipApi\Workslip.slnx --no-restore` -- expected: solution builds without errors.
- `git diff --check` -- expected: no whitespace errors.

## Suggested Review Order

**Authenticated actor resolution**

- Start where `/api/auth/me` deliberately separates actor identity from effective tenant.
  [`AuthService.cs:23`](../../../src/BE/WorkslipApi/Workslip.Application/Auth/AuthService.cs#L23)

- Inspect the signed-actor equality guard and narrowly unscoped database read.
  [`EfUserRepository.cs:21`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfUserRepository.cs#L21)

- Confirm the dedicated contract stays distinct from tenant-scoped user lookup.
  [`IUserRepository.cs:9`](../../../src/BE/WorkslipApi/Workslip.Application/Users/IUserRepository.cs#L9)

- Follow delegated-session verification through the same actor-only boundary.
  [`OrganizationSessionService.cs:36`](../../../src/BE/WorkslipApi/Workslip.Application/Organizations/OrganizationSessionService.cs#L36)

**Isolation evidence**

- Reproduce platform-home `/me` through SQLite while projecting NP Teknik.
  [`AuthServiceEntraLoginTests.cs:54`](../../../src/BE/WorkslipApi/Workslip.Tests/Auth/AuthServiceEntraLoginTests.cs#L54)

- Verify profile update, tenant update, and delete remain tenant-scoped.
  [`AuthServiceEntraLoginTests.cs:106`](../../../src/BE/WorkslipApi/Workslip.Tests/Auth/AuthServiceEntraLoginTests.cs#L106)

- Confirm session issuance calls actor lookup without general user lookup.
  [`OrganizationSessionServiceTests.cs:15`](../../../src/BE/WorkslipApi/Workslip.Tests/Organizations/OrganizationSessionServiceTests.cs#L15)

**Supporting changes**

- Keep invitation test doubles explicit after the repository contract addition.
  [`InvitationEnrollmentTests.cs:292`](../../../src/BE/WorkslipApi/Workslip.Tests/Invitations/InvitationEnrollmentTests.cs#L292)

- Track unrelated SQLite fixture failures separately from this auth fix.
  [`deferred-work.md:14`](deferred-work.md#L14)
