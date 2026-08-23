# ADR 0011: Customer portfolio UI belongs in MR SAAS'y, not product SuperAdmin

- Status: Superseded
- Date: 2026-08-16
- Tracking: WOR-635
- Superseded by: ADR 0013
- Related: WOR-447, WOR-552, WOR-591, WOR-592, WOR-694; ADR 0009, ADR 0010, ADR 0007, ADR 0013

## Supersession note

ADR 0013 supersedes the part of this decision that retained product-owned SuperAdmin surfaces. The customer-portfolio ownership and adapter/projection boundaries below remain useful historical context, but the privileged cross-product/cross-tenant SuperAdmin/Admin operating surface is now owned by MR SAAS'y Spacecenter.

## Context

Operators need one place to see customer accounts, the product instances those accounts run, modules or capabilities they have purchased, and websites or properties managed on their behalf.

Workslip already has a SuperAdmin surface for in-product administration (organizations, users, cache and diagnostics). Expanding that surface into a cross-product customer portfolio would couple platform customer operations to a single product UI and make a later multi-product MR SAAS'y extraction harder.

MR SAAS'y Control Center is already defined as a provider-neutral, read-oriented platform surface (ADR 0009). Its planned information architecture covers Overview, Leadership, Delivery, Agents, Approvals and Applications/Operations (see `Docs/agents/CONTROL_CENTER_OPERATING_MODEL.md`). It does not yet define a Customers / Accounts section, and Executive UI work remains tracked under WOR-591 / WOR-592.

Website tenant branding work (WOR-447) is data-driven on the marketing site and does not provide an operator catalogue of managed customer sites.

Without an explicit placement decision, portfolio features risk landing in Workslip SuperAdmin by convenience rather than by architecture.

## Decision

1. The canonical **customer portfolio** — accounts/customers, projects/product instances, entitlements/modules purchased or enabled, and managed websites/properties — is owned by **MR SAAS'y**.
2. Product SuperAdmin UIs (including Workslip SuperAdmin) remain limited to **in-product** administration for that product’s tenants, users and operational tools. **This item is superseded by ADR 0013 for the privileged SuperAdmin/Admin operating surface.**
3. Products contribute portfolio data only through **adapters / projections**. The platform must not import product domain schemas, repositories or DTOs as the portfolio source of truth (consistent with ADR 0009 and ADR 0010).
4. Platform **Account** identity may correlate to product tenants (for Workslip: `OrganizationId` per ADR 0007) without becoming a second authorization boundary inside the product.
5. v1 portfolio is **read-oriented** where possible. Write paths (enable module, publish site, change entitlement) stay in the owning system or become explicit, authorized platform actions — not silent dual-writes of product state.
6. Control Center information architecture gains a **Customers / Accounts** section alongside the existing planned surfaces.

## Consequences

### Positive

- one operator home for cross-product customer state;
- Workslip SuperAdmin stays thin and product-scoped;
- portfolio UX can evolve with MR SAAS'y extraction instead of being trapped in Workslip FE;
- adapter boundary keeps product domain data local and reduces PII/schema leakage into platform core;
- requests to “show all customers’ modules/sites in SuperAdmin” have a clear redirect target.

### Costs

- portfolio UI does not exist until Control Center shell and contracts land;
- Account / Project / Entitlement / ManagedWebsite contracts and product adapters must be designed before a polished UI;
- short-term operators may still use Workslip SuperAdmin plus external notes until the Sassy surface ships;
- entitlements and managed-website models are not fully implemented in products yet, so early portfolio views will be partial projections.

## Non-goals

- replacing Workslip SuperAdmin for in-product org/user admin; **superseded by ADR 0013 where this refers to the privileged SuperAdmin/Admin platform entry point**;
- turning Control Center into a CRM, billing system or universal business warehouse (ADR 0009 non-goals still apply);
- copying customer PII or full product tables into the platform domain model;
- full invoicing or commercial quote-to-cash in v1;
- requiring Workslip SuperAdmin changes as a prerequisite for this decision.

## Implementation guidance

Suggested slices (not all required by this ADR alone):

1. Record the decision (this ADR + WOR-635).
2. Define Account / Project / Entitlement / ManagedWebsite read contracts and adapter ports.
3. Add Customers / Accounts to the Control Center UI shell (after or with WOR-591 / WOR-592 as appropriate).
4. Ship a Workslip adapter that projects organization summaries and, when available, module and site references.

Website branding configuration (WOR-447) remains product/site data work. **Listing and operating** those sites across customers is this portfolio surface.

## References

- `Docs/architecture/adr/0007-filial-under-organization.md`
- `Docs/architecture/adr/0009-platform-control-center-read-model.md`
- `Docs/architecture/adr/0010-mr-saasy-control-plane-bootstrap-boundary.md`
- `Docs/architecture/adr/0013-mr-saasy-spacecenter-privileged-admin-surface.md`
- `Docs/agents/CONTROL_CENTER_OPERATING_MODEL.md`
- Linear: WOR-635, WOR-447, WOR-552, WOR-591, WOR-592, WOR-694
