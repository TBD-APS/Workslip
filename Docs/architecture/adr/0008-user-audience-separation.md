# ADR 0008: Separate internal test identities from customer user audiences

**Status:** Accepted  
**Date:** 2026-08-10

## Context

Workslip needs internal QA identities that exercise the real application with ordinary tenant roles such as `User` and `Admin`. Encoding that distinction in `Role` would weaken the meaning of authorization roles, while hard-coding test e-mail addresses or user IDs would create hidden policy and operational coupling.

The existing Superadmin user-management stack already owns cross-organization creation and maintenance of tenant identities. The audience distinction therefore belongs on the user identity itself and must be managed through that stack rather than through a parallel administration endpoint.

## Decision

Add `UserKind` as an identity-audience dimension separate from `Role`:

- `Member` is the normal customer audience and the default for existing users, existing pending invitations and Superadmin-created users.
- `InternalTest` identifies internal QA identities.
- `Role` continues to control authorization. `UserKind` does not grant permissions.
- Non-Superadmin user discovery, direct user management and assignment targeting are restricted to the authenticated actor's `UserKind` audience in addition to the existing Organization, Filial and role rules.
- Superadmin may view and administer both audiences through the cross-organization user-management flow.
- Authentication lookup deliberately does not filter by `UserKind`; an internal test identity must be able to sign in and exercise its real role.
- Direct tenant user creation inherits the creator's audience. A `Member` Admin creates `Member` users; an `InternalTest` Admin creates `InternalTest` users.
- Invitations persist the inviter's audience and enrollment creates the user in that same audience. Re-sending a pending invitation may not silently move it between audiences; the existing invitation status must first be cleared.
- Superadmin creation defaults to `Member` unless `InternalTest` is selected explicitly. Superadmin may reclassify an existing tenant user.
- No e-mail address, Entra ID or Workslip user ID is used as the definition of an internal test identity.

## Data and lifecycle

`UserKind` is persisted on `Users` and on pending `InviteTokens`. Existing rows are migrated to `Member`.

Changing `UserKind` changes future user discovery, management and assignment eligibility. It does **not** rewrite or delete historical jobs, assignments, worksheets, audit events or other operational records created by that identity. Historical records retain their ordinary tenant ownership and authorization rules.

`UserKind` is not a retention, deactivation or legal-basis mechanism.

## Security consequences

Audience filtering is enforced server-side; frontend filtering is not a security boundary. Unknown or missing audience values fail closed for non-Superadmin user/assignment operations.

The audience restriction composes with, rather than replaces:

- `OrganizationId` tenant isolation;
- `FilialId` assignment scope;
- role-based authorization;
- Superadmin protection rules.

## Privacy impact

`UserKind` is an operational classification attached to a user identity and is therefore personal-data-related metadata. The change narrows discoverability of internal QA identities. It introduces no new external processor, transfer, telemetry payload or retention behavior. Application logs use IDs/role/audience metadata where operationally necessary and do not add hard-coded personal test identities.

## Consequences

The model supports realistic internal QA accounts without inventing test-only roles or hidden identity lists. Superadmin user management becomes the explicit place to classify these accounts. Any future requirement to isolate the **operational data created by** internal test users would be a separate data-partitioning decision; this ADR only separates identity audiences.
