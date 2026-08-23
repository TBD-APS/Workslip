# MR SAAS'y Spacecenter (Control Center) operating model

**State:** Active

**Tracking:** WOR-552, WOR-585, WOR-586, WOR-588, WOR-591, WOR-596, WOR-601, WOR-694

This document is the maintained shared operating model for how humans and AI agents should understand the MR SAAS'y Spacecenter / Control Center. `Spacecenter` is the canonical human-facing privileged admin/operator UI; `Control Center` remains the architectural name for the normalized platform/read-model family behind it. Repository code, accepted ADRs and Linear remain authoritative for implementation and live delivery status.

## 1. Purpose

Spacecenter is the common privileged operating surface for MR SAAS'y and its registered products/services. It should let Founder/operators, SuperAdmins and scoped Admins understand company priorities, delivery state, automation state, application/service health, access scope and evidence without hunting through product-specific admin pages or raw provider conversations.

It is not a replacement for Linear, GitHub, repository documentation, release evidence, telemetry or product-owned data.

The platform should reduce operational attention cost: one place to see what matters, what failed, what is stale, what requires approval, what may be operated and why the displayed state is believed.

## 2. Source-of-truth boundaries

Use the owning systems as truth:

- **Repository/code/tests/configuration** — implemented technical behaviour.
- **ADRs and maintained repository docs** — accepted architecture and operating rules.
- **Linear** — issue scope, priority, ownership, dependencies and delivery lifecycle.
- **GitHub** — repositories, branches, pull requests, commits, reviews, checks, Actions workflow runs and source release evidence.
- **Release/deployment evidence** — what revision was actually promoted and verified.
- **Telemetry/incident sources** — observed runtime health and incidents.
- **Provider adapters** — normalized observations about agent/runtime activity.
- **Product adapters** — minimized product/account/tenant projections and explicit authorized action contracts.

Spacecenter stores or consumes normalized state, timestamps, correlation identifiers and evidence references. It must not silently overwrite conflicting source state.

`UNKNOWN`, `BLOCKED`, `STALE`, `DEGRADED` and conflicting evidence are first-class visible outcomes. Missing evidence must never be coerced green.

## 3. Human roles and authorization boundary

### Founder / governance authority

The human Founder/Chair remains the highest governance authority. Founder authority may be represented through a platform role with additional approval policy, but it is not inferred from an AI provider/model identity.

### SuperAdmin

SuperAdmin may receive cross-application/cross-tenant platform visibility and explicitly authorized privileged actions, including identity/access administration, service configuration, operational controls and break-glass workflows where separate policy permits.

SuperAdmin does not bypass product data minimization, audit requirements or human approval gates.

### Admin

Admin is always scoped to assigned applications, accounts, tenants or environments. It may only see and operate the resources granted by the control-plane authorization model.

Admin cannot:

- elevate itself to SuperAdmin;
- expand its own grants;
- gain Spacecenter access merely because it has broader GitHub permissions;
- bypass product tenant/data boundaries.

UI visibility is not command authorization. Every privileged command is authorized server-side against role, resource and policy.

## 4. Company hierarchy

The organizational hierarchy is role-based, not provider-based:

```text
Founder / Chair
    |
    v
AI CEO
    |
    +-- COO / Chief of Staff
    +-- CTO
    +-- CPO
    +-- CMO / Growth
    +-- Finance / Commercial
            |
            v
    department orchestrators
            |
            v
      specialist agents
```

A provider/model is an assignment to a role, not the identity of the role. Concrete model IDs belong in configuration and run provenance.

Executive agents consume normalized company/delivery evidence rather than raw transcripts by default.

## 5. Executive authority boundaries

AI executives may analyze, recommend, prioritize and coordinate reversible work within configured policy.

Human approval remains required for material or irreversible decisions including:

- pricing changes;
- contracts or legal commitments;
- material spend outside configured limits;
- hiring/firing or employment commitments;
- equity/ownership;
- destructive production actions;
- material public company statements with legal/reputational risk;
- changes to the governance rules that constrain the executive agent itself.

An executive agent cannot expand its own permissions, budget or approval authority.

## 6. Agent delivery model

All agents use the same conceptual routing primitives:

```text
Provider -> Model -> Role -> Policy -> Run
```

Relevant role groups include executive leadership, engineering orchestration and implementation, architecture/review, QA/security/SRE, product/research and commercial/content specialists.

Implementation and approving review must preserve separation of duties. An implementation agent/model must not be the sole approving reviewer for its own change.

## 7. Canonical Spacecenter information architecture

The human UI converges on one navigation model. Do not create separate top-level dashboards per provider, department or product unless a future ADR changes this boundary.

### Overview

The operator landing surface answers:

- What needs attention now?
- What needs approval?
- What is blocked, stale, degraded or unknown?
- Which apps/services and environments are healthy?
- Which delivery or operational risks are material?
- Which agents/workstreams are active?

### Apps & Services

The primary operational catalogue for registered applications and services. Workslip and future products/services appear as peers.

Each entry should expose, where available:

- owner and type;
- environment(s);
- health/readiness/freshness;
- current or last deployed revision;
- GitHub repository/default branch;
- relevant Actions workflows and latest run state;
- release/deployment evidence;
- telemetry/incidents;
- current blockers/stale/degraded/unknown findings;
- next recommended action and evidence links.

Adding an app/service should be registration/adapter driven, not a new frontend architecture.

### Action Queue

One prioritized inbox for findings requiring operator attention: unhealthy services, failed/stale automations, blocked releases, high-risk bugs, stale workstreams, delivery evidence mismatch and documentation drift.

### Delivery

Correlates delivery evidence without becoming a second issue tracker:

```text
Linear issue
  -> branch / PR
  -> review / CI
  -> merge SHA
  -> release / deployment
  -> production verification
  -> documentation/content evidence
```

### Workforce

Shows registered roles/providers/models, capabilities, current/latest run, freshness, cost/usage metadata where available, capacity state and evidence links.

Role is the primary identity; provider/model is metadata.

### Customers / Accounts

Shows cross-product portfolio projections such as accounts, product instances, entitlements/modules and managed properties/sites. Product data enters only through explicit minimized adapters/projections.

### Access

Shows identities, role grants, resource scope, authorization evidence and break-glass state. SuperAdmin/Admin boundaries are explicit.

### Connections

Shows health/configuration state for GitHub, Linear, Azure, AI providers and future integrations. Browser clients never receive integration secrets.

### Audit / Evidence

Shows normalized evidence for important state and operator actions, including source references, timestamps, actor/role, scope and outcome.

## 8. GitHub operational boundary

GitHub is a first-class operational adapter/evidence source, not the Spacecenter authorization system.

Read-side examples include:

- repository/default branch metadata;
- relevant PR/review/check state;
- Actions workflow catalogue and run history;
- release/deployment evidence;
- deep links to owning GitHub pages/logs/configuration.

Spacecenter should summarize operator-relevant state and link back to GitHub for source detail rather than becoming a GitHub clone.

Later write-side operations may include explicitly allow-listed retry/dispatch commands. These must execute through the control plane with server-side authorization, resource scope checks and audit evidence. Repository/cloud/provider credentials are never exposed to the browser.

## 9. Product Delivery Drift model

The Control Center read model must make delivery mismatches visible without inventing source-system truth.

Examples include:

- active work with no meaningful checkpoint beyond configured threshold;
- stale PR review/CI/blocker;
- merged PR with unresolved issue lifecycle mismatch;
- completed issue missing expected merge/release evidence;
- merged change absent from expected release/deployment;
- deployed change lacking required verification evidence;
- urgent/high confirmed bug without an owner;
- reopened regression without an active next action;
- user-facing release missing required documentation evidence.

Every drift finding should retain finding type/rule, severity, detected time, source freshness, affected app/workstream, owner where known, recommended next action and evidence references.

## 10. Documentation lifecycle and agent responsibility

Important decisions must not remain only in chat/provider transcripts.

Use:

- ADRs for significant durable architecture/security/privacy decisions;
- maintained docs/AGENTS/handbook for current operating rules;
- Linear for work scope/status/dependencies/blockers;
- PRs for implementation-specific review and validation evidence.

Agents must preserve issue/PR/release/docs correlation when available and surface known drift rather than hiding it in conversational summaries.

Do not copy every repository document into Spacecenter storage. Repository documents remain authoritative; the platform keeps normalized references and freshness/drift state.

## 11. Handbook/context freshness

The shared agent handbook and operating context are versioned inputs to agent work.

Agents should report the loaded handbook/context source revision when the provider supports it. Spacecenter may mark an agent `WARN`, `STALE` or `BLOCKED` when it is materially behind required governance/context according to policy.

A governance/context update is not complete merely because the chat that created it ended; the decision must be represented in maintained repository documentation or Linear and propagated through the onboarding/context contract.

## 12. Current implementation status

### Accepted/current direction

- Spacecenter is the canonical privileged human surface for SuperAdmin/Admin operations (ADR 0013).
- MR SAAS'y owns the privileged cross-product/cross-tenant admin entry point; Workslip remains a product system.
- Control Center uses a provider-neutral normalized read model (ADR 0009).
- GitHub is a first-class operational evidence adapter but not the canonical authorization model.
- Agent identity separates role from provider/model.
- Product Delivery Drift is a projection over source evidence, not a duplicate tracker.
- Action Queue is the common prioritized attention surface.
- Raw private transcripts, secrets and customer PII are excluded from the central projection by default.

### Planned implementation

Delivery is tracked in Linear. The immediate foundation is WOR-597/WOR-605, then WOR-596 for the standalone role-scoped shell and WOR-601 for Apps & Services with GitHub-backed operational evidence. WOR-598, WOR-568, WOR-600 and WOR-603 extend the same shell rather than creating parallel dashboards.
