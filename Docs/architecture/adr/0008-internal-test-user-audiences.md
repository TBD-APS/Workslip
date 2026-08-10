# ADR 0008: Separate internal test identities from customer user audiences

**Status:** Accepted  
**Date:** 2026-08-10

## Context

Workslip uses non-production identities to exercise real `User`, `Admin`, `Auditor` and platform flows against deployed environments. Some of those identities need to belong to the same Organization and Filial model as ordinary users so authorization, assignment and workflow behaviour is representative.

A role cannot identify these accounts because the account must retain the role being tested. A generic hidden flag would also mix two different concerns: authorization/role semantics and the audience in which a user identity is discoverable.

Customer actors must not discover, manage or assign internal QA identities merely because those identities are stored in the same Organization. Internal QA actors still need to discover and assign other internal QA identities so real multi-user flows remain testable.

## Decision

`Users` has a separate `UserKind` classification with exactly two current values:

- `Member` — ordinary organization users;
- `InternalTest` — Workslip-controlled identities used for internal QA/release testing.

`Role` remains the authorization and functional-role dimension. `UserKind` does not grant permissions and does not replace Organization or Filial ownership.

All existing users and ordinary newly created/invited users default to `Member`. Classification as `InternalTest` is explicit and is restricted to Superadmin operations; no email address or user ID is hard-coded as a test identity.

For non-Superadmin actors, user discovery, user management and job-assignment targets are restricted to the actor's own `UserKind` inside the existing Organization/Filial boundaries. This creates two audiences inside an Organization:

```text
Organization
├── Member audience
│   ├── User
│   └── Admin
└── InternalTest audience
    ├── User
    └── Admin
```

A `Member` Admin therefore cannot discover or assign an `InternalTest` user, while an `InternalTest` Admin can exercise the same flows against other `InternalTest` identities. Superadmin may administer both audiences. The existing rule that ordinary actors cannot manage/discover `Superadmin` identities remains a separate role/security rule.

Authentication and identity lookup do not filter by `UserKind`; otherwise an `InternalTest` identity could not authenticate and test its real role. Existing history, worksheets and assignments keep their user foreign keys and are not deleted or rewritten when the classification changes.

## Consequences

- Internal QA identities can exercise production-like role behaviour without appearing in ordinary customer user or assignment lists.
- User visibility is fail-closed when a non-Superadmin actor's `UserKind` cannot be resolved.
- Pagination/count queries apply the same audience filter as the user rows they return.
- The assignment API enforces audience isolation server-side in addition to Organization, Filial and assignment-role rules; frontend filtering is not a security boundary.
- Reclassifying a user changes future discovery/management/assignment audience. It does not erase historical data or automatically remove existing assignments.
- `UserKind` is not an activation/deactivation or retention mechanism. Account lifecycle remains a separate concern.
- This decision partitions **user identities**, not all data produced by test identities. Jobs created by an internal test account still follow the normal Organization/Filial job visibility rules. Hiding test-generated job data would require a separate product/data-isolation decision.

## Personal-data impact

The change adds an operational classification to existing user records and narrows who can discover those records. It introduces no new external processor, transfer or authentication bypass. Existing retention/deletion obligations for user records remain unchanged; the classification must not be used as a substitute for test-data retention and cleanup policy.
