# Workslip agent delivery handoffs

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** root `AGENTS.md`, the owning Linear issue, current repository state, active PRs and exact-head validation evidence  
**Review cadence:** When agent responsibilities or delivery gates materially change

This document operationalizes the repository-wide delivery rules in [`../../AGENTS.md`](../../AGENTS.md). It does not create a second workflow, extra Linear states or a separate source of product truth.

Use it to move one cohesive issue between product triage, repository inspection, planning, implementation, review, validation and release readiness without relying on chat history.

## Parallel-session coordination

Multiple agents or ChatGPT sessions may work in Workslip at the same time. Before editing:

1. inspect active branches and pull requests related to the issue, feature and files you expect to change;
2. inspect the owning Linear issue and its recent comments;
3. if another active session owns the same issue or overlapping files, coordinate in a durable PR or Linear comment before editing;
4. choose one owner for overlapping scope instead of creating competing implementations;
5. when work is genuinely independent, record the boundary and continue in separate cohesive branches.

Do not use chat messages as the only coordination mechanism. A future session must be able to reconstruct ownership from Linear, GitHub and the repository.

If an active stack or PR already owns the required implementation, extend or coordinate with it rather than silently recreating the same change.

## Standard handoff

Every agent-to-agent handoff should be short enough to scan and complete enough that the next agent does not need a chat transcript.

```text
Issue: <ID — title>
Goal: <one sentence>
Verified facts: <source-backed facts only>
Scope: <work owned by this issue/step>
Non-scope: <explicit exclusions>
Risks: <material risks only>
Artifacts: <Linear/PR/files/evidence>
Next allowed action: <specific next step>
Stop/escalate if: <conditions>
```

Do not copy large repository summaries into the handoff when the referenced source already exists.

## Role boundaries

| Role | Owns | Must not turn into |
|---|---|---|
| Product triage | customer problem, value decision, smallest valuable slice, success metric | implementation design |
| Technical scout | read-only repository truth, reuse points, affected boundaries, verified risks | speculative architecture or code edits |
| Planner | smallest complete implementation, non-scope, ordering and evidence plan | a platform redesign |
| Builder | cohesive implementation and justified regression protection | unrelated cleanup |
| Adversarial reviewer | plausible production failures and verified findings | cosmetic review noise |
| QA / validator | cheapest strong evidence for the changed risk | test-count or coverage maximization |
| Release gate | exact-head readiness decision | inference from generic green CI |

Product triage for customer-facing work is defined in [`CUSTOMER_VALUE_GATE.md`](CUSTOMER_VALUE_GATE.md). Validation selection is defined in [`VALIDATION.md`](VALIDATION.md).

## Technical scout

The scout is read-only.

### Input

- owning issue and product decision;
- root and scoped `AGENTS.md` files;
- current implementation, configuration, schema, tests and accepted ADRs;
- relevant active branches and PRs.

### Output

- verified current state;
- affected layers and boundaries;
- existing components/services/contracts to reuse;
- material risks and unknowns;
- likely implementation boundary;
- any parallel-session overlap discovered.

### Stop or escalate

Stop when there is a corrupted or mixed worktree, conflicting accepted architecture, exposed secrets/production personal data, or when the observed problem is materially different from the issue being scoped.

Do not stop for normal implementation details that can be resolved from the repository.

## Planner

The planner converts verified repository facts into the smallest complete implementation.

### Must define

- implementation sequence and affected boundaries;
- explicit non-scope;
- authorization, tenant, data-integrity, concurrency and integration implications when material;
- migration and rollback implications when applicable;
- which evidence is required and why.

Do not introduce a generalized workflow, abstraction, dependency or infrastructure layer unless the current issue proves the need.

### Stop or escalate

Stop for destructive production actions, irreversible data semantics, a missing material product/legal decision, a new processor/data transfer decision, or scope that no longer fits one cohesive PR.

## Builder

The builder implements the approved issue scope using established project boundaries and shared components.

When implementation reveals another problem:

- **same-scope correctness, security or data-integrity bug:** fix it;
- **directly related but separable improvement:** create or link follow-up work;
- **unrelated finding:** report it without changing it.

Do not weaken authorization, tenant isolation, validation or tests to make the change easier to ship.

The builder hands off the exact branch/PR, changed behavior, known risks and validation that still needs to run.

## Adversarial reviewer

Primary question:

> Assume this PR contains a production bug. What is the most plausible way it fails?

Review in this order when applicable:

1. tenant isolation, IDOR and role escalation;
2. data integrity, historical semantics and transaction boundaries;
3. partial failure, retries, idempotency and concurrency;
4. stale cache, identity and authorization state;
5. external integration failure and unsafe fallback behavior;
6. frontend state, mobile/responsive behavior and important error/recovery paths;
7. maintainability only when it creates a concrete correctness or operational risk.

Do not generate findings for formatting preferences, style already enforced by tooling, speculative abstractions or missing tests without naming the regression they would protect.

Each material finding must state:

- severity;
- verified evidence;
- affected file or boundary;
- plausible production failure mode;
- recommended correction;
- whether regression protection is justified and which type.

The review passes when no verified blocker or unresolved high-risk correctness finding remains inside the issue scope.

## QA / validator

Primary question:

> Which real production failure is plausible, and what is the cheapest strong evidence that proves we protected against it?

Choose evidence according to [`VALIDATION.md`](VALIDATION.md):

- **Unit** for real business rules, calculations, state transitions and deterministic edge cases;
- **Postman feature/API** as the primary backend feature boundary for HTTP, authorization, persistence and coherent multi-endpoint workflows;
- **Playwright** for critical changed user-visible browser flows.

Build, lint, typecheck, OpenAPI consistency, schema checks and docs checks are engineering gates. They are not reasons to manufacture additional regression tests.

No test-count or coverage target is part of this contract.

## Release gate

Release readiness uses the exact PR/candidate SHA and produces one decision:

- **READY** — required evidence exists and no blocker remains;
- **BLOCKED** — required evidence or a blocker remains unresolved;
- **READY WITH EXPLICIT EXCEPTION** — the accountable owner explicitly accepts a named remaining risk and records that exception durably in the PR or Linear.

A generic green CI result is not enough when the changed risk required evidence that CI did not execute.

## Global stop and continue rules

### Stop or escalate

- destructive production operation;
- irreversible data semantics or uncontrolled migration;
- unclear tenant or authorization boundary;
- exposed secret/security material;
- new external processor, data transfer or legal/product approval requirement;
- scope becoming a different cohesive issue;
- accepted architecture requiring reversal;
- production release requiring an explicit owner risk exception.

### Continue without interrupting the product owner

- ordinary implementation choices inferable from source;
- bugs that can be diagnosed and safely fixed inside scope;
- routine build or test failures;
- trivial missing details recoverable from repository or Linear;
- cosmetic uncertainty that does not change behavior or risk.

## Linear state mapping

Keep the existing Linear workflow simple. Internal agent stages do not become statuses.

| Linear state | Typical agent activity |
|---|---|
| Backlog | product triage / validate demand |
| Todo | scout and planning may start |
| In Progress | scout → planner → builder |
| In Review | adversarial review → QA/validation → release readiness |
| Done | delivered state and evidence match reality |

Outcome review or a delivery retro is only needed for meaningful releases, larger batches, incidents or product bets. Record at most one or two durable process improvements; do not create process work from every small PR.
