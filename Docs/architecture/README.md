# Architecture documentation

**Status:** Active index  
**Owner:** Architecture owner  
**Review cadence:** When trust boundaries, persistence, authentication, deployment topology or major dataflows change

Current implementation is the technical source of truth. This area records durable decisions and the small amount of system context that is expensive to rediscover from code. Prioritization and implementation sequencing belong in Linear rather than a parallel architecture plan.

## Current architecture views

- [`accounting-integration-boundary.md`](accounting-integration-boundary.md) — operational boundary between Workslip job economics and external accounting providers such as e-conomic.
- [`domain-and-dataflows.md`](domain-and-dataflows.md) — tenant ownership and core data-integrity boundaries.
- [`dependency-map.md`](dependency-map.md) — generated module coupling map; regenerate with `node tools/depmap/depmap.mjs`.
- [`figma-design-environment.md`](figma-design-environment.md) — colour-token ownership, the Figma export contract and design-file layout.
- [`frontend-design-accessibility.md`](frontend-design-accessibility.md) — authenticated frontend visual system, semantic tokens and accessibility baseline.
- [`frontend-stylesheet-boundaries.md`](frontend-stylesheet-boundaries.md) — ownership and migration rules for the legacy `App.css` boundary.
- [`github-actions-convergence.md`](github-actions-convergence.md) — boundary between thin GitHub Actions execution and MR SAAS'y-owned agent runtime/orchestration.
- [`job-repository-composition.md`](job-repository-composition.md) — ownership boundary between Jobs application assignment policy and infrastructure repository decorators.
- [`workslip-docs.md`](workslip-docs.md) — product Docs trust boundary, persistence and repository-documentation separation.

## Draft architecture proposals

- [workslip-modular-product-blueprint.md](workslip-modular-product-blueprint.md) — proposed customer-selectable Workslip module model, live-only pricing/Shopify commerce plan, entitlement controls, onboarding and boundary prerequisite order.

Business-domain split priorities and delivery sequencing are tracked in Linear under WOR-443 and its child issues. Use the generated dependency map plus current code as technical evidence; do not maintain a second issue plan here.

Useful future views, when they can be kept concise and stable:

- `system-context.md` — users, external systems and trust boundaries.
- `containers.md` — frontend, API, SQL, Azure services and external integrations.

Do not create a page merely to fill the list. Add it only when it reduces rediscovery cost without copying implementation detail.

## Accepted decisions

- [`adr/0001-managed-identity-runtime-and-secret-lifecycle.md`](adr/0001-managed-identity-runtime-and-secret-lifecycle.md)
- [`adr/0002-immediate-pwa-update-activation.md`](adr/0002-immediate-pwa-update-activation.md)
- [`adr/0003-github-infrastructure-oidc-bootstrap.md`](adr/0003-github-infrastructure-oidc-bootstrap.md)
- [`adr/0003-vapid-key-rotation-and-subscription-repair.md`](adr/0003-vapid-key-rotation-and-subscription-repair.md)
- [`adr/0004-retire-maintained-repository-snapshots.md`](adr/0004-retire-maintained-repository-snapshots.md)
- [`adr/0005-main-as-production-boundary.md`](adr/0005-main-as-production-boundary.md)
- [`adr/0006-explicit-database-migrations-with-deployment-identity.md`](adr/0006-explicit-database-migrations-with-deployment-identity.md)
- [`adr/0007-filial-under-organization.md`](adr/0007-filial-under-organization.md)
- [`adr/0008-private-blob-storage-for-images.md`](adr/0008-private-blob-storage-for-images.md)
- [`adr/0008-job-costing-billing-basis.md`](adr/0008-job-costing-billing-basis.md)
- [`adr/0008-user-audience-separation.md`](adr/0008-user-audience-separation.md)
- [`adr/0009-platform-control-center-read-model.md`](adr/0009-platform-control-center-read-model.md)
- [`adr/0010-mr-saasy-control-plane-bootstrap-boundary.md`](adr/0010-mr-saasy-control-plane-bootstrap-boundary.md)
- [`adr/0012-central-domain-field-copyability-policy.md`](adr/0012-central-domain-field-copyability-policy.md) — central copy/call/e-mail and future domain-field interaction policy.
- [`adr/0013-mr-saasy-spacecenter-privileged-admin-surface.md`](adr/0013-mr-saasy-spacecenter-privileged-admin-surface.md) — MR SAAS'y Spacecenter owns the privileged SuperAdmin/Admin surface and GitHub-backed app/service operations.
- [`adr/0014-mr-saasy-delivery-lifecycle-orchestration-boundary.md`](adr/0014-mr-saasy-delivery-lifecycle-orchestration-boundary.md) — MR SAAS'y owns cross-product delivery-lifecycle orchestration; Workslip remains the focused product owning system.
- [`adr/0015-workslip-module-access-consumer-contract.md`](adr/0015-workslip-module-access-consumer-contract.md) — how Workslip consumes per-tenant module entitlement and gates capabilities (server `IWorkslipModuleAccess`, frontend `FeatureGate`, product-owned adapter boundary).
- [`adr/0016-generic-job-evidence-checklist-module.md`](adr/0016-generic-job-evidence-checklist-module.md) — carve VVS "installations/KLS" into a trade-agnostic Job Evidence / Checklist module; Workslip owns the engine, builder, packs and content, MR SAAS'y owns only entitlement and price.
- [`adr/0017-ai-retrieval-belongs-to-mr-saasy-agent-runtime.md`](adr/0017-ai-retrieval-belongs-to-mr-saasy-agent-runtime.md) — retrieval (RAG) and model/provider routing, including Cerebras, live in the MR SAAS'y agent runtime; Workslip requests provider-neutral operations and keeps the trusted/untrusted context split.
- [`adr/0020-azure-monitor-is-the-telemetry-boundary.md`](adr/0020-azure-monitor-is-the-telemetry-boundary.md) — Workslip emits telemetry and logs to Azure Monitor and delivers them nowhere; consumers read the Log Analytics workspace with a read-only role, cross-tenant via Lighthouse.

## Superseded decisions

- [`adr/0011-customer-portfolio-ui-in-mr-saasy.md`](adr/0011-customer-portfolio-ui-in-mr-saasy.md) — superseded by ADR 0013 where it retained product-owned SuperAdmin surfaces; its customer-portfolio and adapter-boundary rationale remains historical context.

Two historical accepted ADRs already use number `0003`. Keep their filenames stable so existing links do not break; allocate the next new ADR number after the highest existing number and do not create another duplicate.

## ADR states

An ADR is `Proposed`, `Accepted`, `Superseded` or `Rejected`. Accepted ADRs are normative decisions; inspect implementation/configuration when verifying current runtime behaviour.
