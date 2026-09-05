# Workslip modular product blueprint

**Status:** Draft

**Owner:** Workslip leadership

**Decision scope:** Customer-selectable product capabilities, not a commitment to microservices or a pricing model

**Depends on:** ADR 0014 — MR SAAS'y owns cross-product delivery-lifecycle orchestration

## Purpose

Make Workslip plug-and-play for a Danish field-service business: an Admin chooses a useful outcome, completes a short guided setup, invites the team and reaches the first real job without a bespoke product variant or an implementation project.

The target is a **modular product on one Workslip runtime**, not an arbitrary code-plugin marketplace. A module is a customer-visible capability with a clear outcome, owner, dependencies, entitlement, setup contract, authorization boundary and lifecycle. It is installed by configuration and onboarding, not by allowing third-party code to enter the product process.

## Research findings

### Product and market

- Workslip's current strategy is a simple workflow and documentation layer for Danish field-service businesses, rather than an ERP. The core outcome is jobs, hours, documentation, approvals and job economics with less coordination. See [WORKSLIP_STRATEGY.md](../strategy/WORKSLIP_STRATEGY.md).
- For the chosen Danish trades, quality documentation is not cosmetic. An authorized business' KLS includes organization/competence information, task staffing and documentation of how work is performed; the KLS must remain valid. [Sikkerhedsstyrelsen — KLS requirements](https://www.sik.dk/erhverv/elinstallationer-og-elanlaeg/vejledninger/elinstallationer/generelt/krav-om-kvalitetsledelses-system-kls-el-vvs-kloak-gas-eller-nedrivning-asbest)
- A frictionless, repeatable tenant onboarding path shortens time to value. AWS also warns that one-off customer versions undermine a SaaS operating model; tenant-specific configuration in one product version is the appropriate compromise. [AWS SaaS Lens — onboarding and tenant customisation](https://docs.aws.amazon.com/wellarchitected/latest/saas-lens/operate.html)

### Architecture and commercial controls

- Module boundaries must follow business capabilities and high cohesion rather than technical layers. They should be revised as evidence changes; a bounded context is not automatically a microservice. [Microsoft — domain analysis for microservices](https://learn.microsoft.com/en-nz/azure/architecture/microservices/model/domain-analysis)
- A product feature needs a stable identifier that can be granted/revoked independently of a commercial plan. Stripe's entitlement model illustrates the separation: features are mapped to products, while access is granted from the customer's effective subscription. [Stripe — Entitlements](https://docs.stripe.com/billing/entitlements)
- **Entitlements, release flags and permissions are different controls.** Feature flags separate deployment from release and support staged rollout or a safety kill-switch; they must not become the billing or authorization source of truth. [Microsoft — Feature flags](https://learn.microsoft.com/en-us/dotnet/architecture/cloud-native/feature-flags)
- Every module boundary must preserve server-side authorization and tenant isolation. Multi-tenant access control needs a consistent policy decision and API enforcement path; a user can be authenticated yet still require tenant-scoped authorization. [AWS — multi-tenant API authorization](https://docs.aws.amazon.com/prescriptive-guidance/latest/saas-multitenant-api-access-authorization/introduction.html)

## Verified current state

| Finding | Evidence | Implication |
| --- | --- | --- |
| The product is a modular monolith in practice, with Application areas for Auth, Jobs, Customers, Documents, Worksheets, Organizations, Users and more. | [Workslip.Application](../../src/BE/WorkslipApi/Workslip.Application) and [dependency map](dependency-map.md) | Improve these module boundaries before considering independent services. |
| Jobs is the largest, most coupled Application area: 3,829 LOC, coupling 8; it has direct edges to Auth, Worksheets and Notifications. | [dependency-map.md](dependency-map.md) | Split Jobs internally by responsibility; do not extract it as a service in one step. |
| Organization, tenant filtering, audit and finalization invariants already protect critical data. | [domain-and-dataflows.md](domain-and-dataflows.md), [JobStatusTransitionPolicy](../../src/BE/WorkslipApi/Workslip.Domain/JobStatusTransitionPolicy.cs), [WorksheetFinalizationGuard](../../src/BE/WorkslipApi/Workslip.Infrastructure/Schema/WorksheetFinalizationGuard.cs) | Modules must extend—not bypass—tenant isolation, approval history or immutable approved work. |
| The backend has role-based authorization and the frontend uses the same permissions for route/navigation UX. | [AuthPolicies](../../src/BE/WorkslipApi/Workslip.Infrastructure/Configuration/AuthPolicies.cs), [permissions.ts](../../src/FE/src/providers/permissions/permissions.ts) | Add a server module gate alongside current role checks; never rely on hidden navigation or frontend flags. |
| There is no tenant entitlement or module catalog in the domain/API. The existing billing screen is explicitly a future guide. | [BillingGuide.tsx](../../src/FE/src/features/settings/routes/BillingGuide.tsx) | Build an entitlement foundation before packaging or selling modules. |
| The MR SAAS'y control plane no longer lives in this repository, and Workslip holds no adapter, credential or address for it. Consumers read Workslip's authorized endpoints instead. | [ADR 0014](adr/0014-mr-saasy-delivery-lifecycle-orchestration-boundary.md) | Saassy may coordinate catalog/evidence later, but Workslip needs a local, safe enforcement projection for normal product use. |

## Target context map

~~~text
                     MR SAAS'y (cross-product catalog, evidence, operations)
                                        │ minimized adapter/projection
                                        ▼
┌──────────────────────── Workslip: one deployable product runtime ────────────────────────┐
│ Foundation: workspace, identity, tenant isolation, roles, audit, files                     │
│      │                                                                                      │
│      ▼                                                                                      │
│ Work management ─────► Time & job economics                                                 │
│      │  ├────────────► Compliance & evidence                                                │
│      │  └────────────► Field collaboration                                                  │
│      └───────────────► Insights & exports (read projections)                               │
│                                                                                             │
│ Integration adapters consume/publish explicit contracts; they never share the domain DB.   │
└─────────────────────────────────────────────────────────────────────────────────────────────┘
~~~

An arrow means a documented contract or published event/read projection, not permission for a module to query another module's persistence tables.

## Proposed module catalog

| Module | Customer outcome | Current evidence and initial scope | Dependencies | Commercial posture |
| --- | --- | --- | --- | --- |
| **Foundation — Workspace & access** | The company, team and data are safely ready to work. | Organizations, Filials, identity, invitations, user/role management, tenant context, audit and secure file primitives. | None | Always on; not sold separately. |
| **Work management** | Create, assign, execute and close a job for a customer. | Jobs, minimal customer record/snapshot, work kinds, assignments, links, status lifecycle and reference data. Customer management remains deliberately job-oriented, not a CRM. | Foundation | Included in every usable package. |
| **Time & job economics** | Register time and see the job's effort and internal economics. | Worksheets, time validation/finalization, employee rate history, monthly hours output and cost/projection contracts. | Foundation, Work management | Included in the initial **Daily work** package; may be separately packaged later only if customer research supports it. |
| **Compliance & evidence** | Produce reviewable KLS/job evidence and a durable handover. | Installation/checklist snapshots, closure flags, controlled Docs, job evidence/attachments, review/approval, auditor view and report/PDF output. | Foundation, Work management; may consume Time read data | Primary add-on/preset for authorized trades. Sector packs are configuration/templates, not code forks. |
| **Field collaboration** | Capture field context and resolve work without side channels. | Job images, job conversation, targeted notifications and assignment context. | Foundation, Work management | Bundle with Daily work initially; later separate only if it becomes independently valuable. |
| **Insights & exports** | Answer operational questions without changing operational records. | Overview, Power BI/CSV/PDF projections and job/time/compliance read models. | Read contracts from enabled modules | Add-on after the source modules have stable projection contracts. |
| **Integrations** | Connect Workslip to an external system without turning Workslip into an ERP. | No concrete customer integration is currently implemented. Add each integration behind an explicit adapter and a specific customer problem. | Relevant published contracts; external processor review when data leaves Workslip | Future module family; validate demand before building. |
| **Saassy operations** | Cross-product readiness, delivery evidence, agent work and privileged operations. | Spacecenter/Control Center, catalog coordination and approved operational commands. | Explicit product adapter only | Platform capability, not a Workslip customer module. |

## Customer packages and activation experience

Customers should select outcomes, never a technical component list. The commercial choice and the product entitlement must match: when a customer declines a meaningful outcome, it is absent from the product and the displayed price is lower.

| Customer-facing choice | Product outcome | Price behaviour |
| --- | --- | --- |
| **Job flow** (required starting point) | Create, assign, execute and close jobs, with the practical field context required to do that work. It includes Work management and Field collaboration; Foundation is invisible and never separately charged. | Starting price. |
| **Time & job economics** | Register time and understand the job's effort and internal economics. | Clear add-on; declining it visibly lowers the price and removes its relevant navigation/actions. |
| **Quality & handover** | KLS-oriented checklists, controlled evidence, review/approval and handover. | Clear add-on priced for the company outcome, not a hidden technical setting. |
| **Operational insight** | Read-only reporting, exports and operational overview from enabled source modules. | Clear add-on; unavailable until its enabled sources provide the required read models. |

**Daily work** is the recommended preset for the current ICP: Job flow plus Time & job economics. Quality & handover and Operational insight are opt-in outcomes, not assumed product bloat.

The checkout/onboarding experience must show the resulting monthly price and a plain-language "not included" summary as choices change. A customer may reduce spend by removing an optional outcome, but cannot remove non-negotiable security, tenant isolation, audit or other Foundation controls. Dependencies remain explicit: an option cannot be sold or activated without the outcome it requires.

An activation is complete only when it passes this user-facing definition:

- an Admin has chosen a package and sees its outcome in plain Danish;
- required setup is a short, resumable checklist with an explicit owner;
- optional configuration is deferred until it is needed, rather than blocking first use;
- navigation and create actions show only enabled, role-authorized capabilities;
- a first real job can be created and completed on the intended flow; and
- the setup state, effective entitlements and audit evidence are visible to an authorized Admin.

Existing tenants are migrated with their currently used capabilities enabled. They must never lose access merely because a commercial package catalog has been introduced.

## Control model

### Module manifest

Define a versioned, source-controlled ModuleDefinition for every product module:

~~~text
key, display name, owner, dependency keys, required permissions,
setup checks, data classification, supported routes/actions,
published contracts, deactivation policy, documentation and test evidence
~~~

Definitions are reviewable product/architecture contracts. They are not dynamically downloaded code and cannot register arbitrary routes, data stores or provider credentials at runtime.

### Effective access

For every request, effective access is the intersection of:

~~~text
active tenant entitlement ∩ active release state ∩ user role/permission ∩ tenant/data scope
~~~

- **Entitlement** is the tenant's contractual right to use a customer module. It is stored as a local Workslip projection with source, effective period, status and revision/audit metadata.
- **Release state** is a separate operational flag used for staged rollout, experiment and emergency kill. It can never grant a module a tenant has not been entitled to use.
- **Role/permission** determines which individual users may operate an enabled module.
- **Tenant/data scope** remains the backend authority for every record and file.

The API exposes a read-only effective-capability summary to the frontend for navigation and onboarding. Every protected endpoint, background worker, file operation and export must enforce the same module decision server-side.

### Activation, deactivation and data

Activation uses an idempotent setup workflow: validate prerequisites, provision configuration/templates, record outcome and make the module visible only after readiness succeeds. Database changes follow the existing expand/contract migration and deployment policy.

Deactivation is **not deletion**. The manifest must declare whether data becomes read-only, remains exportable, is retained for a defined period or follows a separately approved deletion path. It must preserve legal/audit obligations and never invalidate an already approved job report.

## Sales site, availability and commerce plan

### Non-negotiable commercial rule

The public price must describe only a capability a new customer can activate and use now. Fast delivery does not make an unimplemented feature a sellable entitlement.

| Commercial availability | Public site treatment | Price and entitlement treatment |
| --- | --- | --- |
| **Available** | Describe verified current capability, price and included/not-included outcome. | May be purchased and provisioned after payment/activation succeeds. |
| **Pilot** | Invite-only description with named readiness limits and feedback expectation. | No standard checkout. Any paid pilot requires a separately approved scope, terms and rollback/exit path. |
| **Coming soon** | Optional interest/waitlist invitation only; do not place it in the price total. | No entitlement, charge or promise of delivery date. A future price-lock offer needs explicit approved terms and expiry. |
| **Not offered / retired** | Do not market to new customers. | No new purchase. Existing-data/read-only treatment follows the module deactivation policy. |

This availability state is distinct from release flags and tenant entitlements. A release flag cannot make a coming-soon feature billable, and a commercial purchase cannot bypass a module's safety/readiness gate.

### Design-partner delivery after purchase

For an early customer, the commercial event can start a **design-partner delivery** rather than grant instant product access. It is an explicitly scoped service/product delivery, not a standard self-service package that silently promises future software.

~~~text
Accepted design-partner order
  → scope and offer confirmed
  → configured / built on the shared product path
  → exact-head qualification
  → import dry-run and customer reconciliation
  → authorized production import
  → customer acceptance and entitlement activation
  → Active
~~~

- The order/contract names the outcomes that exist now, the small additional scope being delivered, the acceptance evidence, the accountable owner, target timing, support path and exit/rollback treatment. It does not say “all future features included”. Subscription charging and any setup/development fee must have separately approved terms; a subscription should not start merely because an internal build has begun.
- Work stays on the common module/configuration path. Customer-specific templates, onboarding data and mappings are configuration; they must not become a private code fork. A new reusable product capability still follows the normal idea → code → review → release readiness path before it can be activated.
- The activation screen remains honest throughout: **Preparing**, **Awaiting customer input**, **Import dry-run failed**, **Ready for approval** and **Active** are different states. A customer never sees an untested component as included/active merely because it was selected in a commercial conversation.
- Import is a separate readiness gate, not a side effect of payment. The current product already supports Admin-scoped customer CSV/XLSX import with validation, duplicate/error reporting and bounded file/row limits; it does not prove a general job, time, document or user migration capability. Promise each data domain only after its mapping, validation, tenant isolation, audit, retention, reconciliation and rollback behaviour have been implemented and tested.
- Shopify receives only the commercial checkout data needed for commerce. Customer operational data is never uploaded to Shopify for “setup”; it enters Workslip only through an approved, tenant-scoped import path after the relevant processor/privacy and customer-authorization checks.

### Intended site experience

The current public site is static marketing and its own rules permit only implemented, validated product claims. The first commercial page therefore ships only when there is at least one **Available** offer and approved public terms/privacy material; it does not turn the current site into an improvised checkout.

The eventual `/priser/` journey should be short and Danish-language:

1. **Choose the current outcome:** Job flow is the required starting point. A visitor can add Time & job economics, Quality & handover or Operational insight only when each is Available.
2. **See the price change:** the running monthly total, billing unit, taxes/terms link, included outcome and an explicit “not included” summary change with every selection. Foundation controls are never optional or priced separately.
3. **Choose the next honest action:** an Available offer leads to checkout; a Pilot/design-partner delivery leads to a clearly scoped conversation or accepted delivery order; Coming soon leads only to interest registration or a non-binding, time-limited price-lock request.
4. **Activate safely:** after purchase, an authorized Admin creates or selects the Workslip organization, confirms the team size and completes the relevant setup checklist. The confirmation view distinguishes purchased, active, pending setup and unavailable capabilities.

Do not show a discounted bundle that contains future capability. A launch discount may apply only to an all-Available bundle, must show its normal price, discount amount, duration and post-promotion price, and must not silently add a later module or charge.

### Shopify as a future commerce edge

Shopify is a candidate **commerce and hosted-checkout provider**, not the Workslip product, entitlement or authorization system. Its product/variant and selling-plan model can represent recurring products and transparent introductory discounts, while Shopify webhooks provide asynchronous order updates. Subscription/purchase-option implementation requires the appropriate Shopify app/access model; Shopify documents that ordinary custom Admin apps cannot use the protected subscription/pre-order/TBYB scopes. Validate the chosen Shopify plan, app model and Danish VAT/invoice requirements before committing to it. [Shopify selling plans](https://shopify.dev/docs/apps/build/purchase-options/subscriptions/selling-plans/build-a-selling-plan), [Shopify purchase options](https://shopify.dev/docs/apps/build/purchase-options), [Shopify webhooks](https://shopify.dev/docs/apps/build/webhooks)

~~~text
Marketing site (/priser) ──purchase intent──► Shopify hosted checkout
                                              │ verified, idempotent event
                                              ▼
MR SAAS'y commerce adapter ──approved entitlement projection──► Workslip
           │                                                        │
           └── account/offer/evidence correlation                  └── local, server-side module enforcement
~~~

- **Marketing site:** presents only the reviewed Available offer catalog and links out to checkout. It must not embed payment secrets, Shopify Admin credentials or raw payment/customer data. Do not add Shopify scripts, analytics or checkout widgets to the static site before the required privacy/vendor decision.
- **Shopify:** owns the selected commercial line items, subscription consent and payment interaction. The checkout description must match the package/discount text shown before checkout; Shopify's subscription UX guidance likewise requires visible savings, plan options and terms. [Shopify subscription UX guidance](https://shopify.dev/docs/storefronts/themes/pricing-payments/subscriptions/subscription-ux-guidelines)
- **MR SAAS'y commerce adapter:** owns the cross-product commercial mapping and correlation to the platform account/product instance. It verifies webhook signatures, deduplicates deliveries, records only minimized commercial evidence/references, reconciles missed events and maps a paid/cancelled/refunded line item to an explicit entitlement command. It never grants product access from a browser callback or by copying a Workslip database.
- **Workslip:** keeps an audited local entitlement projection and remains the authoritative request-time module/tenant/role enforcement point. A Saassy or Shopify outage must not interrupt an already valid customer workflow; it must surface a safe, reconcilable commercial state rather than inventing access.

The first Shopify catalog should contain only separate, readable offers: Job flow, Time & job economics, Quality & handover and Operational insight, plus an explicit start-bundle promotion when all included offers are Available. It must not use an unbuilt-feature SKU, a generic “future features” line item or a customer-specific code fork. Shopify identifiers, promotion rules and the entitlement mapping belong in a reviewed OfferDefinition with: public name, availability, included/excluded outcomes, billable unit, price/currency/tax display, Shopify product/variant/selling-plan references, effective period, activation prerequisites and data/deactivation policy.

### Progressive delivery and gates

These are prerequisite gates, not a second issue plan. Linear remains the authority for implementation priority, ownership and delivery sequencing.

| Step | Smallest complete outcome | Evidence before proceeding |
| --- | --- | --- |
| 0. Validate price language | 2–3 design partners select a live-only package from a static prototype and can explain what they pay for and what is excluded. | Customer Value Gate: **VALIDATE FIRST** until observed willingness-to-pay and objections are recorded. |
| 1. Public pricing page | `/priser/` explains Available versus Coming soon honestly and routes to the correct non-payment CTA. | Approved public terms/privacy copy, browser/mobile evidence, no unverified feature claim and no third-party tracking/payment script. |
| 2. Product foundation | Workslip can store/evaluate local module entitlements and show an Admin the effective capability state. | Authorization/tenant/API/worker/file/export tests, migration compatibility, activation/deactivation policy and existing-tenant migration evidence. |
| 3. Sandbox commerce path | A Shopify sandbox transaction maps through the Saassy adapter to a non-production Workslip tenant and is safely reversible. | Vendor/processor approval, webhook signature/idempotency/replay/reconciliation tests, minimum-data review and a failed-payment/cancellation path. |
| 4. Limited live pilot | An explicitly approved Available offer can be purchased by a small set of customers and activated without manual database work; a scoped design-partner delivery follows its explicit build/import/acceptance path. | Exact-head delivery evidence, human commercial owner, support/recovery runbook, approved delivery scope, import dry-run/reconciliation where data is moved, invoice/tax/terms evidence and post-purchase activation smoke. |
| 5. Expand or stop | Repeat only the offers customers select and retain. | Conversion, activation, upgrade, cancellation and support evidence; stop or revise offers with weak demand rather than filling the catalog. |

Before step 3 or later, the accountable owner must approve the processor/contract, lawful basis, retention/deletion, transfer, support-access, VAT/invoice, refund/cancellation and customer-rights treatment. This document is an architecture/product plan, not that approval.

## Boundary implementation order

Keep one deployable Workslip first. A module boundary becomes an independent service only when evidence shows a need for separate ownership, deployment cadence, scale, data lifecycle or availability characteristics.

This is an architecture prerequisite order, not a second delivery plan. Linear remains the authority for individual issue priority and delivery sequencing under WOR-443 and its child issues.

1. **Foundation seam:** introduce server-side module authorization and local entitlement projection without changing customer-visible behaviour. Migrate all existing tenants to explicit active entitlements.
2. **Work management seam:** make Jobs own the work-order lifecycle and its minimal customer/assignment contracts. Move product-specific detail behind internal contracts instead of adding new direct repository references.
3. **Time seam:** make Worksheets and rate/history logic a Time & job economics module that consumes the published work-order identity/status contract. Preserve current finalization and approval invariants.
4. **Compliance seam:** isolate checklist/template snapshots, controlled documents, review/auditor evidence and report generation behind Compliance & evidence contracts. It depends on a work order but owns its template/evidence vocabulary.
5. **Collaboration and insight seams:** move conversations/images/notifications behind field-collaboration contracts, then build read-only insights projections from published contracts rather than from cross-module joins.
6. **Saassy adapter:** after a stable local product contract exists, publish minimized module/readiness/evidence projection to Saassy. Saassy never becomes Workslip's per-request authorization dependency.

## Evidence gates and success metrics

| Gate | Evidence required |
| --- | --- |
| Customer value | 2–3 design partners from the current ICP can select a package, understand its value and complete first-job onboarding within one day. Validate willingness to pay before treating package boundaries as pricing. |
| Architecture | Every module has a named owner, manifest, dependency graph and published contract. No optional module reads another module's persistence implementation. Dependency-map coupling decreases for the targeted edge. |
| Security | Tenant entitlement and role checks are enforced by API/worker/file/export tests; disabled modules cannot be reached by a direct request; cache keys and organization switches cannot leak effective access. |
| Data lifecycle | Activation is idempotent. Deactivation has a reviewed read-only/export/retention policy and preserves approved/audited data. |
| Delivery | Exact-head CI, migration compatibility, API contract evidence and relevant browser flow pass before a module is enabled for a pilot tenant. |
| Operations | Saassy projection can be stale or unavailable without blocking Workslip. The product shows a safe local state and records any reconciliation drift for operators. |

## Decisions still requiring validation

- Which package boundaries customers will pay for, versus which capabilities must remain in Daily work, require design-partner interviews and a willingness-to-pay test.
- Customer portal, quotes/invoicing, scheduling, materials and external accounting systems are not current module commitments. Each requires a separate customer-value and integration/compliance decision.
- The eventual commercial system of record is undecided. Until a billing provider is selected and approved, Workslip can use manually administered, audited entitlements; it must not pretend the existing Billing Guide is an implementation.
- A cross-product catalog in Saassy is a later control-plane capability. It must complete privacy, processor, authorization and availability design before receiving tenant/product data or becoming a commercial source of truth.

## References

- [WORKSLIP_STRATEGY.md](../strategy/WORKSLIP_STRATEGY.md)
- [domain-and-dataflows.md](domain-and-dataflows.md)
- [dependency-map.md](dependency-map.md)
- [ADR 0009](adr/0009-platform-control-center-read-model.md), [ADR 0011](adr/0011-customer-portfolio-ui-in-mr-saasy.md) (historical portfolio context), [ADR 0013](adr/0013-mr-saasy-spacecenter-privileged-admin-surface.md) and [ADR 0014](adr/0014-mr-saasy-delivery-lifecycle-orchestration-boundary.md)
- [Sikkerhedsstyrelsen — KLS requirements](https://www.sik.dk/erhverv/elinstallationer-og-elanlaeg/vejledninger/elinstallationer/generelt/krav-om-kvalitetsledelses-system-kls-el-vvs-kloak-gas-eller-nedrivning-asbest)
- [AWS SaaS Lens — onboarding and tenant customisation](https://docs.aws.amazon.com/wellarchitected/latest/saas-lens/operate.html)
- [Microsoft — domain analysis for microservices](https://learn.microsoft.com/en-nz/azure/architecture/microservices/model/domain-analysis)
- [Stripe — Entitlements](https://docs.stripe.com/billing/entitlements)
- [Microsoft — Feature flags](https://learn.microsoft.com/en-us/dotnet/architecture/cloud-native/feature-flags)
- [AWS — multi-tenant API authorization](https://docs.aws.amazon.com/prescriptive-guidance/latest/saas-multitenant-api-access-authorization/introduction.html)
