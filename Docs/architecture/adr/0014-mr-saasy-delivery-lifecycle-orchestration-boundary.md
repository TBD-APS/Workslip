# ADR 0014: MR SAAS'y owns cross-product delivery-lifecycle orchestration

- Status: Accepted
- Date: 2026-08-24
- Tracking: Repository-owner governance direction; implementation is separately scoped in Linear
- Extends: ADR 0009, ADR 0010 and ADR 0013

## Context

Workslip is a field-service workflow and documentation product. Its value comes from a simple, reliable path through jobs, time, documentation, approvals and customer integrations. It must remain independently understandable, deployable and operable as a product.

The organization also needs a durable way to coordinate delivery across Workslip and future products: turn an idea into code, independent review, production evidence and follow-up without losing the decision trail, introducing product-specific control rooms or turning each product into an agent platform.

The lifecycle-engineering rules in root `AGENTS.md` apply to all delivery. Their cross-product coordination, evidence correlation, agent routing, action queue and privileged approval concerns belong to MR SAAS'y Spacecenter / Control Center, not to Workslip's product domain.

## Decision

1. **MR SAAS'y owns the cross-product delivery-lifecycle layer.** It coordinates normalized evidence and attention across idea, implementation, independent review, merge, release, production verification, operational follow-up and future product/services.
2. **Workslip remains a focused product owning system.** It owns field-service workflow and documentation behaviour, product-domain authorization, tenant isolation, product data and customer-facing product experience. It does not become the organizational agent runtime, portfolio planner, provider-routing core, cross-product delivery tracker or privileged platform shell.
3. **The normal product delivery path remains fast:** `idea → code → independent review → go live`. MR SAAS'y makes lifecycle evidence and blockers visible inside that path; it does not add a serial workshop or approval process for ordinary low-risk product work.
4. **Integration is adapter-based and minimized.** Workslip may expose only explicit product-adapter projections and authorized commands, such as health/readiness, deployment and verification evidence, and narrowly scoped operator actions. MR SAAS'y must not import Workslip schemas, repositories, raw tenant data or business semantics as control-plane state.
5. **Workslip remains operationally independent.** A MR SAAS'y outage, stale projection or unavailable AI provider must not prevent Workslip's ordinary customer workflows, product-domain authorization, deterministic CI or safe product deployment from operating. `UNKNOWN`, `BLOCKED`, `STALE` and conflicting platform evidence remain visible rather than silently overridden.
6. **Authority remains explicit.** Any command initiated through Spacecenter is authorized by the owning system and retains its existing environment, tenant, human-approval, audit and rollback controls. There are no direct product-database writes, hidden dual-writes or autonomous destructive operations.

## Consequences

### Positive

- Workslip can become simpler and more predictable rather than accumulating company/platform concerns.
- Future products receive the same delivery, evidence and approval model through adapters instead of copying Workslip-specific operational UI and agent logic.
- Spacecenter can expose cross-product readiness and attention without becoming a duplicate issue tracker, GitHub clone or universal business database.
- Product delivery keeps a short path while material risk, compatibility and operational evidence stay traceable.

### Costs

- Each product/service needs a clear, minimized adapter contract for relevant operational evidence and commands.
- Product and platform teams must preserve source-of-truth boundaries and avoid convenient shortcuts such as direct database access or copied domain models.
- The lifecycle view is eventually consistent and must display freshness/unknown state honestly.

## Non-goals

- moving Workslip customer workflows, domain rules, tenant data or product ownership into MR SAAS'y;
- making MR SAAS'y a mandatory runtime dependency for ordinary Workslip use or delivery;
- centralizing raw CI logs, product databases, customer PII or product business semantics;
- replacing Linear, GitHub, product CI/CD, telemetry or product-specific release gates;
- adding a new mandatory meeting/approval sequence before low-risk changes can ship;
- authorizing autonomous destructive production actions.

## Implementation guidance

This ADR changes the ownership boundary, not an application's code in one batch. Future work must be separately scoped and validate the relevant adapter, authorization, provenance, freshness and failure-mode risks. Workslip simplification is incremental: move a concern only when MR SAAS'y has the explicit contract and safe operating evidence to own it.

## References

- `AGENTS.md` — Lifecycle engineering
- `Docs/strategy/WORKSLIP_STRATEGY.md`
- `Docs/agents/CONTROL_CENTER_OPERATING_MODEL.md`
- `Docs/architecture/adr/0009-platform-control-center-read-model.md`
- `Docs/architecture/adr/0010-mr-saasy-control-plane-bootstrap-boundary.md`
- `Docs/architecture/adr/0013-mr-saasy-spacecenter-privileged-admin-surface.md`
