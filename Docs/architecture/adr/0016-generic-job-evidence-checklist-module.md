# ADR 0016 — Generic Job Evidence / Checklist module

**Status:** Accepted

**Owner:** Workslip architecture owner

**Decision scope:** Turn the VVS-specific "installations / KLS" feature into a trade-agnostic, tenant-configurable **Job Evidence / Checklist** module, and fix where its parts live across Workslip and MR SAAS'y. Builds on ADR 0015 (module-access consumer contract) and the [modular product blueprint](../workslip-modular-product-blueprint.md).

## Context

"Installations" today is the KLS quality-control flow (installation types → control categories → control points, checked off per job). In practice the machinery is already **trade-agnostic and data-driven**: a job carries a checklist template plus per-job snapshots, and the whole catalogue is org-scoped reference data seeded from one file. Only three things are actually VVS-specific:

1. a `JobType.KLS` enum that leaks into a few job-create/closure paths,
2. hard-coded Danish labels in the wizard steps and validation messages, and
3. the seed content itself (the Danish KLS categories and points).

We want other trades to use the same capability, to let tenants tailor it, and to package/sell it as a module. This raised the real question this ADR answers: **where does such a module live when it is toggled in MR SAAS'y but implemented by Workslip?**

## Decision

### Ownership split (the core decision)

A "module" is three separate things, and they do not live in the same place:

- **Implementation** — the checklist engine, the template builder, the prebuilt packs, and all template/snapshot content — lives in **Workslip**. It is product domain and org-scoped; it never leaves Workslip.
- **Entitlement** — whether a tenant may use the module (on/off) — lives in **MR SAAS'y**, and Workslip consumes it through the ADR 0015 module-access adapter. Enforcement stays server-side in Workslip.
- **Pricing** — the per-module price and the total-cost computation across products — lives in **MR SAAS'y** only. Workslip may *display* a price by reading it from the platform contract, but never stores or authors pricing.

In one line: **MR SAAS'y owns whether the module is on and what it costs; Workslip owns what the module is and does.** This is the same control-plane / data-plane split as ADR 0015, and it preserves the platform boundary (no product domain or product data in the platform).

### Shape of the module

- A job references a **data-driven checklist template**, not a hard-coded job type; completion rules (e.g. "at least one checked point per relevant category") become template configuration.
- **Self-serve builder + prebuilt packs:** tenants create and edit their own templates in the UI (the engine already supports org-scoped reference data), and we ship **prebuilt packs** — VVS/KLS first — as starting points to clone. Prebuilt packs and all content stay in Workslip.
- The module is gated by the ADR 0015 `FeatureGate` / server module-access path. When it is off, the job wizard falls back to the simple job flow that already exists.

## Carve-out order (high level)

1. **Decouple from KLS** — replace the `JobType.KLS` enum with a checklist-template reference; move completion rules to template config; migrate existing KLS jobs onto a VVS/KLS template. *Only risky phase — touches job create/closure and needs a data migration.*
2. **Prebuilt packs** — turn the single seed into named, versioned packs; onboarding clones a chosen pack into the tenant's editable templates.
3. **Self-serve builder** — CRUD over the existing org-scoped reference tables. Guardrail: a template already bound to a finalized report cannot rewrite history (per-job snapshots already protect this).
4. **Externalize labels** — i18n the Danish strings so a pack/template carries its own names.
5. **Register the module** — entitlement + pricing via MR SAAS'y (ADR 0015 path).

The schema, snapshot tenant-integrity rules, PDF composition, and reference-data endpoint need no structural change — roughly 80% of the work is decoupling and configuration, not a rewrite.

## Consequences

- Workslip stays the single owner of the feature; MR SAAS'y owns only the commercial switch and price, so a new per-trade pack is a pricing/catalog change in the platform, not a Workslip rewrite.
- Existing VVS customers keep KLS as the first pack with no loss of function; the migration maps them onto it.
- The boundary stays clean: no checklist content and no product data ever live in MR SAAS'y.

## References

- [Workslip modular product blueprint](../workslip-modular-product-blueprint.md)
- ADR 0015 — Workslip module-access consumer contract (the entitlement/adapter path this module is gated by)
- [ADR 0014 — MR SAAS'y delivery-lifecycle orchestration boundary](0014-mr-saasy-delivery-lifecycle-orchestration-boundary.md)
- [ADR 0010 — MR SAAS'y control-plane bootstrap boundary](0010-mr-saasy-control-plane-bootstrap-boundary.md)
