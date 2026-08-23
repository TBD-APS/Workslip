# ADR 0013: MR SAAS'y Spacecenter owns privileged admin and app/service operations

- Status: Accepted
- Date: 2026-08-23
- Tracking: WOR-591, WOR-694, WOR-596, WOR-601
- Supersedes: ADR 0011 where it retained product-owned SuperAdmin surfaces
- Related: WOR-552, WOR-554, WOR-597, WOR-605, WOR-505, WOR-516, WOR-517; ADR 0009, ADR 0010

## Context

MR SAAS'y is becoming a multi-application operating platform. Operators need one privileged surface for application/service health, delivery evidence, access, approvals, customer/account projections and operational actions.

Historically, Workslip contained a product-local SuperAdmin surface. ADR 0011 moved the cross-product customer portfolio into MR SAAS'y but still retained product-owned SuperAdmin UIs. That split now creates overlapping admin concepts and encourages new operational surfaces to be added wherever implementation is convenient.

Linear decisions WOR-591 and WOR-694 establish a broader direction: the privileged SuperAdmin/platform-administration surface belongs to MR SAAS'y. Workslip and future products remain owning systems for product state and expose only explicit adapters, projections and authorized actions.

GitHub already owns important operational truth for repositories, branches, pull requests, checks, Actions and release evidence. The platform should make that evidence easy to operate from without copying GitHub into a second source of truth.

## Decision

1. MR SAAS'y owns one standalone privileged operating UI, named **Spacecenter**. `Control Center` remains a compatible architectural term for the underlying platform/read-model family.
2. Spacecenter is the canonical human operating surface for **SuperAdmins** and **Admins** across registered applications and services.
3. **SuperAdmin** may receive cross-application/cross-tenant platform visibility and explicitly authorized privileged commands.
4. **Admin** is always resource-scoped to assigned applications, accounts or tenants. Admin cannot expand its own grants, change itself to SuperAdmin or inherit privilege from an external integration implicitly.
5. Workslip and future products retain product-domain authority. Product-local UI may continue to support ordinary tenant-scoped workflows, but the privileged cross-tenant/platform entry point is not owned by Workslip.
6. Products expose Spacecenter data through explicit adapter/projection contracts. The platform does not import product schemas, repositories, DTOs or raw tenant databases as its domain model.
7. Spacecenter navigation converges on one model: **Overview, Apps & Services, Action Queue, Delivery, Workforce, Customers / Accounts, Access, Connections, Audit / Evidence**.
8. **Apps & Services** is configuration/adapter driven. Adding a product, service or environment must not require a new frontend architecture or provider-specific navigation branch.
9. GitHub is a first-class operational adapter and evidence source for repository metadata, PR/review/check state, Actions workflow runs, releases/deployments and deep links to source logs/configuration.
10. GitHub is not the canonical Spacecenter authorization model. Repository permissions never silently become platform/product permissions.
11. Any write from Spacecenter is an explicit server-authorized command against the owning system. There are no silent dual-writes of product state.
12. Destructive or material production actions remain subject to explicit approval policy and evidence/audit requirements.
13. `UNKNOWN`, `STALE`, `BLOCKED`, `DEGRADED` and conflicting evidence remain first-class states. Missing evidence must never be rendered as healthy.

## Role boundary

### SuperAdmin

Typical platform scope may include:

- cross-application/cross-tenant operational visibility;
- identity/access administration;
- service configuration and connection health;
- privileged release/automation controls where explicitly allowed;
- break-glass workflows under separate policy;
- audit/evidence access appropriate to the role.

SuperAdmin still does not bypass product data minimization or approval policy.

### Admin

Admin may operate only resources assigned by the control-plane authorization model. Typical scope may include:

- assigned application/service health;
- assigned tenant/account operations;
- permitted deployment/automation actions;
- relevant delivery evidence and incidents.

Admin cannot self-elevate, edit its own effective grants or gain access because it happens to have broader GitHub access.

## GitHub integration boundary

Spacecenter should summarize operator-relevant GitHub state and link back to GitHub for source detail. It should not become a GitHub clone.

Read-side examples:

- repository/default branch;
- open/relevant PR state;
- review/check status;
- Actions workflow catalogue and run status;
- release/deployment evidence;
- source/log links and freshness.

Later command-side examples may include allow-listed retry/dispatch actions. Those commands must execute through the control plane with server-side authorization, scope checks and audit evidence rather than by exposing repository credentials to the browser.

## Consequences

### Positive

- one predictable privileged operating surface instead of product/provider-specific admin dashboards;
- Workslip stays product-scoped and does not become the accidental platform shell;
- GitHub operational evidence becomes easy to scan while GitHub remains authoritative;
- new products/services can join through adapters and registration rather than bespoke frontend branches;
- SuperAdmin/Admin scope becomes explicit and testable;
- customer, delivery, automation and access evidence converge without creating a universal business database.

### Costs

- standalone Spacecenter shell/auth/BFF work must land before the full UX exists;
- adapter contracts are required for each owning system;
- product-local admin entry points may need gradual redirect/deprecation work;
- command-side operations require stronger authorization/audit design than read-only views.

## Supersession of ADR 0011

ADR 0011 remains useful historical context and its customer-portfolio/adapters decision still holds. It is superseded only where it states that product SuperAdmin UIs, including Workslip SuperAdmin, remain the privileged in-product administration home.

The new rule is:

- MR SAAS'y Spacecenter owns the privileged cross-product/cross-tenant SuperAdmin/Admin operating surface;
- products retain product state and ordinary tenant-scoped workflows;
- cross-product projections and privileged operations flow through explicit platform contracts.

## Non-goals

- replacing Linear, GitHub or telemetry as source systems;
- copying full product databases or customer PII into the control plane;
- turning Spacecenter into CRM, billing or a universal data warehouse;
- provider-specific organizational/navigation trees;
- browser-held GitHub/cloud/provider secrets;
- autonomous destructive production actions.

## Implementation guidance

Delivery sequencing remains in Linear:

1. WOR-597 — stable standalone BFF/read contract.
2. WOR-605 — visual/status/accessibility semantics.
3. WOR-596 — standalone Spacecenter shell, auth and role-scoped navigation.
4. WOR-601 — Apps & Services catalogue, GitHub operations and run evidence.
5. WOR-598 / WOR-568 — Overview and Action Queue.
6. WOR-600 / WOR-603 — Delivery/Approvals and Workforce.

Existing read/evidence work such as WOR-552, WOR-554 and WOR-594 feeds these surfaces rather than creating separate dashboards.

## References

- `Docs/architecture/adr/0009-platform-control-center-read-model.md`
- `Docs/architecture/adr/0010-mr-saasy-control-plane-bootstrap-boundary.md`
- `Docs/architecture/adr/0011-customer-portfolio-ui-in-mr-saasy.md`
- `Docs/agents/CONTROL_CENTER_OPERATING_MODEL.md`
- Linear: WOR-591, WOR-694, WOR-596, WOR-601, WOR-552, WOR-554
