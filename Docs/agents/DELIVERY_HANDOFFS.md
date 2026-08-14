# Workslip agent delivery handoffs

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** root `AGENTS.md`, the owning Linear issue, current repository state, active PRs and exact-head validation evidence  
**Review cadence:** When agent responsibilities or delivery gates materially change

This operationalizes [`../../AGENTS.md`](../../AGENTS.md). It does not create another workflow, extra Linear states or a separate source of product truth.

## Parallel-session coordination

Before editing when multiple agents/sessions are active:

1. inspect related active branches/PRs and the owning Linear issue;
2. compare expected scope and files;
3. if scope overlaps, coordinate in a durable PR/Linear comment and choose one owner;
4. extend the existing implementation when it already owns the work;
5. if work is independent, record the boundary and continue separately.

Do not use chat as the only coordination record.

## Standard handoff

```text
Issue: <ID — title>
Goal: <one sentence>
Verified facts: <source-backed facts only>
Scope: <owned work>
Non-scope: <explicit exclusions>
Risks: <material risks only>
Artifacts: <Linear/PR/files/evidence>
Next allowed action: <specific next step>
Stop/escalate if: <conditions>
```

Reference source artifacts instead of copying long chat summaries.

## Role contracts

| Role | Input | Owns | Handoff | Stop/escalate |
|---|---|---|---|---|
| Product triage | request + customer evidence | problem, value decision, smallest valuable slice, success metric | product scope + non-scope | material target-customer, pricing, legal-purpose or strategy decision |
| Technical scout | issue + repo/AGENTS/ADRs/active PRs | read-only current state, reuse points, affected boundaries, verified risks | facts, reuse, risks, unknowns, overlap | corrupted/mixed worktree, conflicting accepted architecture, exposed secret/prod data, issue mismatch |
| Planner | triage + scout | smallest complete implementation, ordering, non-scope, risk/evidence plan | implementation sequence + required evidence | destructive action, irreversible data semantics, missing material decision, scope no longer cohesive |
| Builder | approved scope + plan | cohesive implementation + justified regression protection | exact branch/PR, changed behavior, known risks, missing evidence | blocker cannot safely be resolved inside issue |
| Adversarial reviewer | issue + PR diff + source | plausible production failures and verified findings | blocker/findings or pass | unresolved blocker/high-risk correctness finding |
| QA / validator | changed risks + implementation | cheapest strong evidence | executed evidence + gaps | required evidence cannot safely run and no exception exists |
| Release gate | exact SHA + findings + evidence | READY / BLOCKED / READY WITH EXPLICIT EXCEPTION | durable readiness decision | unresolved blocker or unaccepted risk |

Customer-facing product triage is defined in [`CUSTOMER_VALUE_GATE.md`](CUSTOMER_VALUE_GATE.md). Test selection is defined in [`VALIDATION.md`](VALIDATION.md).

## Builder finding rule

When implementation reveals another problem:

- **same-scope correctness/security/data-integrity bug:** fix it;
- **directly related but separable improvement:** create/link follow-up work;
- **unrelated finding:** report it only.

Do not weaken authorization, tenant isolation, validation or tests to make a change easier to ship.

## Adversarial review

Primary question:

> Assume this PR contains a production bug. What is the most plausible way it fails?

Review these risks when applicable, in roughly this order:

1. tenant isolation, IDOR and role escalation;
2. data integrity, historical semantics and transaction boundaries;
3. partial failure, retries, idempotency and concurrency;
4. stale cache, identity and authorization state;
5. external integration failure and unsafe fallback behavior;
6. frontend state, mobile behavior and important error/recovery paths;
7. maintainability only when it creates concrete correctness/operational risk.

Do not generate cosmetic/style findings already covered by tooling, speculative abstractions or test requests without a named regression risk.

Every material finding states severity, evidence, affected boundary, production failure mode, recommended correction and whether a regression test is justified.

## QA / validation

Ask:

> Which real production failure is plausible, and what is the cheapest strong evidence that proves we protected against it?

Default choices:

- **Unit:** real business rules, calculations, important state transitions, deterministic edge cases.
- **Postman feature/API:** primary backend feature boundary for HTTP, auth, persistence and coherent multi-endpoint workflows.
- **Playwright:** critical changed user-visible browser flows.

Build, lint, typecheck, OpenAPI, schema and docs checks are engineering gates, not reasons to manufacture more regression tests. No test-count or coverage target applies.

## Release readiness

Use the exact PR/candidate SHA and output exactly one:

- **READY** — required evidence exists and no blocker remains.
- **BLOCKED** — required evidence/blocker remains unresolved.
- **READY WITH EXPLICIT EXCEPTION** — accountable owner accepts a named remaining risk in PR/Linear.

Generic green CI is not sufficient when the changed risk required evidence CI did not execute.

## Global stop / continue

**Stop or escalate:** destructive production operation; irreversible/uncontrolled migration; unclear tenant/auth boundary; exposed secret; new processor/data transfer/legal approval; scope becoming another issue; accepted architecture requiring reversal; production release requiring owner risk acceptance.

**Continue without interrupting the product owner:** normal implementation choices inferable from source; bugs safely fixable inside scope; routine build/test failures; trivial details recoverable from repo/Linear; cosmetic uncertainty without behavior/risk impact.

## Linear mapping

Keep existing statuses; internal agent stages are not workflow states.

| Linear state | Typical activity |
|---|---|
| Backlog | product triage / validate demand |
| Todo | scout + planning |
| In Progress | scout → planner → builder |
| In Review | adversarial review → QA/validation → release readiness |
| Done | delivered state and evidence match reality |

Run outcome review/retro only for meaningful releases, larger batches, incidents or product bets. Record at most one or two durable process improvements.
