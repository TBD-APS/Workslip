# MR SAAS'y Control Center operating model

**State:** Active

**Tracking:** WOR-552, WOR-585, WOR-586, WOR-588, WOR-591, WOR-592

This document is the maintained shared operating model for how humans and AI agents should understand the MR SAAS'y Control Center. It complements `AGENT_HANDBOOK.md`; repository code, accepted ADRs and Linear remain authoritative for implementation and live delivery status.

## 1. Purpose

The Control Center is the normalized operating projection for MR SAAS'y and its products. It should let the Founder and AI leadership understand company priorities, delivery state, product drift, automation state, application health and evidence without reading raw provider conversations or copying source-system data into a new universal database.

It is not a replacement for Linear, GitHub, repository documentation, release evidence or product-owned data.

## 2. Source-of-truth boundaries

Use the owning systems as truth:

- **Repository/code/tests/configuration** — implemented technical behaviour.
- **ADRs and maintained repository docs** — accepted architecture and operating rules.
- **Linear** — issue scope, priority, ownership, dependencies and delivery lifecycle.
- **GitHub** — branches, pull requests, commits, checks and review evidence.
- **Release/deployment evidence** — what revision was actually promoted and verified.
- **Provider adapters** — normalized observations about agent/runtime activity.
- **Help & Academy/content evidence** — user-facing documentation completion where release governance requires it.

Control Center stores normalized state, timestamps, correlation identifiers and evidence references. It must not silently overwrite conflicting source state.

`UNKNOWN`, `BLOCKED`, `STALE` and conflicting evidence are first-class visible outcomes.

## 3. Company hierarchy

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

The human Founder/Chair remains the highest authority.

A provider/model is an assignment to a role, not the identity of the role. Concrete model IDs belong in configuration and run provenance.

Executive agents consume normalized company/delivery evidence rather than raw transcripts by default.

## 4. Executive authority boundaries

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

## 5. Agent delivery model

All agents use the same conceptual routing primitives:

```text
Provider -> Model -> Role -> Policy -> Run
```

Relevant role groups include:

- executive leadership;
- engineering orchestration and implementation;
- architecture and review;
- QA/security/SRE;
- product and research;
- content/market/commercial specialists.

Implementation and approving review must preserve separation of duties. An implementation agent/model must not be the sole approving reviewer for its own change.

## 6. Control Center UI information architecture

The planned company-OS UI should converge on one navigation model instead of creating separate dashboards per provider or department.

### Overview

Founder cockpit answering:

- What are the current company priorities?
- What needs approval?
- What is blocked or stale?
- What delivery/product risk needs attention?
- Which agents/workstreams are active?
- Are registered applications/environments healthy?

### Company / Leadership

Shows the role hierarchy, current model assignment, current workstream, latest meaningful checkpoint, escalation state and evidence.

Provider/model should appear as metadata/badges. Role is the primary identity.

### Delivery

Shows feature, bug, documentation, release and verification state through the Product Delivery Drift projection.

### Agents

Shows registered roles/providers/models, capabilities, current/latest run, freshness, cost/usage metadata where available and evidence links.

### Decisions & Approvals

Shows material executive recommendations/decisions, evidence and human approval requirements.

### Applications / Operations

Shows application/environment health, releases, deployments, automations, incidents, metrics freshness and the shared Action Queue.

## 7. Product Delivery Drift model

The Control Center must make delivery mismatches visible without becoming a second issue tracker.

A tracked feature/workstream may correlate:

```text
Linear issue
  -> branch / PR
  -> review / CI
  -> merge SHA
  -> release / deployment
  -> production verification
  -> documentation / content evidence
```

A tracked bug may correlate:

```text
report / triage
  -> confirmed reproduction
  -> owner / fix PR
  -> regression evidence
  -> release / deployment
  -> post-release verification
  -> close or reopen
```

### Delivery drift examples

- active work with no meaningful checkpoint beyond configured threshold;
- stale PR review/CI/blocker;
- merged PR with unresolved issue lifecycle mismatch;
- completed issue missing expected merge/release evidence;
- merged change absent from expected release/deployment;
- deployed change lacking required verification evidence.

### Bug drift examples

- urgent/high confirmed bug without an owner;
- severity-dependent stale bug;
- merged fix not verified/closed;
- reopened regression without active next action;
- bug linked to failed/reverted release evidence.

### Documentation drift examples

- user-facing release missing a docs-impact decision;
- docs-impact requires update/new content but evidence is missing;
- architecture-affecting change requires maintained docs/ADR work that is still unresolved;
- active agent is materially stale against the required handbook/context revision;
- Help & Academy content is behind the relevant verified release;
- duplicate/superseded maintained docs remain active without a clear pointer.

A drift finding is an observation produced by a rule and evidence. It is not allowed to invent source-system truth.

Every drift finding should retain:

- finding type/rule;
- severity/attention level;
- detected time;
- source freshness;
- affected application/workstream;
- owner/responsible role where known;
- recommended next action;
- evidence references.

## 8. Action Queue

The Action Queue is the prioritized operator surface for findings that require action.

It can combine operational and product-delivery findings such as:

- unhealthy/degraded services;
- failed/stale automations;
- blocked releases;
- high-risk bugs;
- stale features/workstreams;
- unresolved delivery evidence mismatch;
- documentation drift requiring action.

The Drift Board remains the richer investigation/correlation surface; the Action Queue remains the prioritized work-to-attend surface.

Default ordering should consider risk/severity and age, not only event recency.

## 9. Documentation lifecycle and agent responsibility

Important decisions must not remain only in chat/provider transcripts.

Use:

- ADRs for significant durable architecture/security/privacy decisions;
- maintained docs/AGENTS/handbook for current operating rules;
- Linear for work scope/status/dependencies/blockers;
- PRs for implementation-specific review and validation evidence.

Agents must preserve issue/PR/release/docs correlation when available and surface known drift rather than hiding it in conversational summaries.

Where useful for drift/release checks, maintained artifacts should expose or be correlatable to:

- owner;
- related issue/feature/release;
- document/content state;
- last verified revision/release;
- source/evidence references;
- superseded/stale pointer.

Do not copy every repository document into Control Center storage. Repository documents remain authoritative; Control Center keeps normalized references and freshness/drift state.

## 10. Handbook/context freshness

The shared agent handbook and operating context are versioned inputs to agent work.

Agents should report the loaded handbook/context source revision when the provider supports it. Control Center may mark an agent `WARN`, `STALE` or `BLOCKED` when it is materially behind required governance/context according to policy.

A governance/context update is not complete merely because the chat that created it ended; the decision must be represented in maintained repository documentation or Linear and propagated through the onboarding/context contract.

## 11. Current implementation status

### Accepted/current direction

- Control Center uses a provider-neutral normalized read model (ADR 0009).
- Agent identity separates role from provider/model.
- Executive leadership remains subordinate to Founder/Chair authority.
- Product Delivery Drift is a projection over source evidence, not a duplicate tracker.
- Action Queue is the common prioritized attention surface.
- Raw private transcripts, secrets and customer PII are excluded from the central projection by default.

### Planned implementation

The Executive Command Center UI and Product Drift Board are planned under WOR-591 and WOR-592. Their presence in this document describes the accepted operating model and intended information architecture; it does not claim those UI surfaces are already implemented.
