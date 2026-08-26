# ADR 0017 — Public enrollment pre-tenant trust model

**Status:** Accepted

**Owner:** Workslip architecture owner

**Decision scope:** Define the Workslip 6.1 public-enrollment trust model before public production signup is enabled. This ADR fixes the identity, tenant and legal-company boundaries that WOR-516, WOR-741, WOR-742, WOR-739 and WOR-743 must implement.

## Context

Workslip's current organization onboarding is an authenticated platform-admin flow, not a public signup flow. `/api/organizations` is mounted under the Superadmin endpoint group, and `CreateOrganizationRequest` lets the trusted caller supply organization and initial-admin fields. The current organization service also requires CVR and rejects an existing CVR before creation.

Current Microsoft authentication maps an external principal onto a Workslip user before tenant claims are added. That mapping still has two properties which are incompatible with public multitenant enrollment:

- `UserClaimsTransformation` can fall back from Entra object ID to email-like claims when looking up a Workslip user.
- `CurrentUserContext.UserId` can fall back from the Workslip-managed `workslipUserId` claim to `NameIdentifier` or `sub` when those values parse as GUIDs.

Therefore a valid Microsoft identity that has no Workslip mapping cannot safely become a normal Workslip user merely by widening the Microsoft authority or removing `RequireSuperAdmin`. Public enrollment needs an explicit pre-tenant state and an immutable provider-subject mapping before normal tenant authorization can apply.

This ADR records the target trust model. It does **not** claim that the dependent implementation slices are already deployed, and it does not itself enable the Microsoft `common` authority or public signup.

## Decision

### 1. Microsoft identity platform is the only 6.1 public enrollment provider

Workslip 6.1 supports Microsoft organizational/work-school accounts and personal Microsoft accounts through the Microsoft identity platform.

We do not add a Workslip-owned password, magic-link or second CIAM stack in the same release. A second provider requires separate evidence that Microsoft-only enrollment materially blocks qualified customers.

Public multitenant authority is enabled only after the immutable external identity contract and Workslip mapping hardening are complete. Until then, the current tenant-bound production login remains the accepted runtime boundary.

### 2. External identity, Workslip user and Workslip tenant are separate identities

The identity model is:

```text
Microsoft issuer + tenant context + immutable subject
                !=
          Workslip User.Id
                !=
      Workslip Organization.Id
```

A valid external Microsoft subject with no Workslip mapping is **pre-tenant**. It is authenticated by Microsoft but has:

- no Workslip `UserId`;
- no Workslip `OrganizationId`;
- no Workslip product role;
- no ability to satisfy normal `RequireUser`, `RequireAdmin` or `RequireSuperAdmin` policies.

Only the dedicated enrollment/bootstrap surface may act on a pre-tenant subject. It uses a short-lived, purpose-bound bootstrap credential rather than a normal tenant JWT.

Normal tenant authorization must derive the internal user identity only from the authoritative Workslip mapping. Provider `sub`, `oid`, `NameIdentifier`, email, `preferred_username` and UPN are not internal Workslip user IDs and do not grant tenant authority by themselves.

WOR-516 owns the provider-neutral external identity contract. WOR-741 owns the Workslip migration/hardening that removes authoritative email linking and makes the external identity key issuer/tenant/immutable-subject aware.

### 3. Public bootstrap creates the real Organization and first Admin atomically

A successful self-service activation creates a real Workslip `Organization` and its first `Admin` through the existing local organization/admin consistency boundary, refactored to accept the verified pre-tenant subject as the identity source.

The first-admin role is a server-side invariant. A public request cannot choose its role, organization scope or any higher authority.

Bootstrap must be idempotent for the same verified external subject and safe under retry/concurrency. External side effects are reconciled around the local atomic creation rather than weakening the local consistency boundary.

WOR-739 owns this implementation.

### 4. Organization.Id is tenant identity; CVR and legal-company data are metadata

`Organization.Id` is the Workslip tenant identity.

CVR, legal name, email domain, Microsoft tenant and similar company data are attributes or evidence about an organization; none of them is the tenant key and none proves ownership by itself.

Initial public activation therefore cannot use raw CVR as first-write-wins ownership, a global tenant identity or a signup-blocking uniqueness claim. If a later workflow requires verified legal-company ownership for billing, integrations or contracts, that must be represented explicitly as a verified company claim with its own evidence and lifecycle.

WOR-742 owns the migration that makes CVR optional for initial activation and removes global CVR uniqueness as the tenant-creation gate. Domain workflows may still require CVR later as a state-driven prerequisite where the actual business/compliance output requires it.

### 5. No parallel provisional workspace aggregate

Workslip does not introduce `ProvisionalWorkspace`, `TrialTenant` or another temporary tenant aggregate for 6.1 enrollment.

The bootstrap surface is temporary state around an external subject; successful activation crosses directly into a real `Organization`. Demo remains a separate concern and must not share production enrollment authority or tenant state.

### 6. Frontend receives explicit authentication/activation state

The frontend state model is explicit:

```text
anonymous
  -> external-authenticated
  -> tenant-session | pre-tenant
  -> bootstrap
  -> tenant-session
```

The frontend must not discover a pre-tenant user by calling ordinary tenant APIs and interpreting `401`/`403`, nor by redirect loops. Backend authentication/session endpoints must expose the state explicitly enough for the frontend to render the correct route without treating authorization failures as enrollment discovery.

WOR-743 owns this implementation.

## Security invariants

Public enrollment is fail-closed around these invariants:

- issuer validation remains enabled for Microsoft multitenant tokens;
- the external identity key includes issuer/tenant context and an immutable provider subject;
- exact audience and required scope are validated;
- email, `preferred_username`, UPN and display names are never authorization or data-identity keys;
- PKCE, state and nonce protections remain intact in browser authentication;
- a pre-tenant/bootstrap credential is short-lived, purpose-bound and cannot satisfy normal tenant policies;
- bootstrap endpoints use replay protection, rate limiting and enumeration-safe errors;
- first `Admin` authority is assigned server-side;
- demo authentication and production enrollment cannot cross trust boundaries;
- raw tokens, secrets and unnecessary PII are not written to telemetry or logs.

These rules compose with ADR 0008's separation of user audience (`UserKind`) from authorization role. `UserKind` remains an audience classification on an already-established Workslip user; it is not an external identity mapping or pre-tenant authority mechanism.

## Delivery gate

This ADR is the architecture gate, not permission to turn public signup on immediately.

Public Microsoft `common` enrollment may be enabled only when the implementation slices provide evidence that:

1. WOR-516/WOR-741 are authoritative for external subject mapping and no email-based authorization linking remains;
2. a pre-tenant principal cannot satisfy ordinary tenant/role policies;
3. WOR-742 has removed CVR as the tenant-creation identity/gate;
4. WOR-739 reuses the local atomic Organization/first-Admin boundary with server-selected authority, idempotency and retry semantics;
5. WOR-743 exposes explicit pre-tenant/activation state without `401`/redirect heuristics;
6. issuer, audience, scope and bootstrap-credential validation fail closed under HTTP/browser verification.

The dependent slices should remain cohesive PRs. They implement this ADR; they do not reopen its core identity semantics independently.

## Migration and rollback stance

The migration is additive and gated:

1. establish the immutable external identity mapping while current tenant-bound login remains supported;
2. remove email and provider-GUID fallbacks as authoritative internal identity paths;
3. decouple CVR from initial tenant creation;
4. add pre-tenant bootstrap and explicit frontend state;
5. enable Microsoft multitenant/public enrollment only after the preceding gates are green.

Rollback before public activation is simply to keep the public enrollment route/authority disabled.

After activation, rollback disables new public enrollment without deleting existing organizations or users. A successfully activated organization is a real tenant and must not be converted back into provisional state. Existing internal Workslip IDs remain stable; external identity mapping migrations must be reversible through explicit data migration/reconciliation, never by reintroducing email-based authorization linking.

## Consequences

- Public signup can be added without weakening existing tenant and role policies.
- Microsoft authentication and Workslip authorization remain separate trust decisions.
- Tenant ownership cannot be squatted merely by knowing a CVR, email domain or Microsoft tenant.
- The product avoids a second temporary tenant model and its reconciliation burden.
- Dependent implementation work has one normative trust model, reducing semantic drift between backend, frontend and identity migrations.
- 6.1 deliberately accepts Microsoft-provider dependency in exchange for a smaller, auditable enrollment scope; another provider is a later evidence-based decision.

## References

- WOR-740 — public enrollment trust-model gate
- WOR-516 — provider-neutral identity contract
- WOR-741 — external identity mapping hardening before multitenant login
- WOR-742 — CVR/legal metadata separation from tenant identity/signup
- WOR-739 — self-service organization + first-admin bootstrap
- WOR-743 — explicit frontend pre-tenant activation state
- [ADR 0008 — separate internal test identities from customer user audiences](0008-user-audience-separation.md)
