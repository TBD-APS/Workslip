# Workslip validation rules

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** executed commands, test output, browser evidence, workflow results and deployed/operational evidence  
**Review cadence:** When validation tooling or release gates change

Validation should answer one question: **what evidence is needed to make the changed risk believable?**

Static inspection, compilation, automated tests, browser tests and deployed smoke are different evidence. Report them separately. Unexecuted tests are intended coverage, not evidence.

## Validation ladder

Use only the levels relevant to the change, but do not skip a lower level when it covers a distinct risk.

1. **Static review** — inspect source, diff, configuration, contracts, boundaries and failure paths.
2. **Build/static tooling** — restore/build, lint/typecheck, analyzers, generated-artifact and documentation checks.
3. **Focused automated tests** — regression tests for the changed rules and edge cases.
4. **Integration** — real HTTP/relational/integration boundaries where behaviour depends on them.
5. **Playwright** — actual changed user flow in a running browser application.
6. **Deployed smoke** — non-destructive verification in the target environment when deployment is in scope.
7. **Operational/compliance evidence** — only when the change affects data lifecycle, processors, legal controls or AI governance.

## Minimum by change type

| Change | Minimum evidence |
|---|---|
| Documentation only | source review + `python tools/docs/check_docs.py` |
| Backend business rule | Release build + focused tests |
| Authorization/tenant boundary | Release build + focused auth/tenant tests + HTTP integration |
| EF Core/schema/transaction | Release build + relational tests + production-data impact review |
| API contract | Release build + focused endpoint tests + OpenAPI/client consistency + HTTP smoke |
| Background service | Release build + focused lifecycle/failure tests; hosted smoke when useful |
| Frontend, no visible behaviour change | lint + tests where useful + production build |
| User-visible frontend | lint + production build + relevant Playwright flow |
| Auth/routing/forms/session/cache/critical flow | Playwright success/failure/recovery plus relevant network/cache checks |
| Infrastructure | syntax/template validation + plan/what-if; deployment smoke only when deployment is in scope |
| External integration | boundary tests + retry/partial-failure review + safe isolated smoke when justified |
| Personal data / processor / AI system | this document **plus** [`../compliance/GDPR_AI_ACT_BASELINE.md`](../compliance/GDPR_AI_ACT_BASELINE.md) |

The matrix is a floor, not a checklist to maximize. Add validation when the actual risk demands it.

## Release-candidate aggregation

A temporary release candidate combines risks that were introduced by multiple PRs. Its readiness evidence must therefore be the union of the still-relevant gates for those changes, not merely one new green aggregate CI run.

Before promoting a release candidate:

- identify the exact candidate SHA and actual diff from the accepted production boundary;
- verify deterministic CI on that exact candidate SHA;
- carry forward unresolved validation from included PRs instead of erasing it when the code is merged into the candidate;
- require SQL Server/relational evidence for migration, constraint, transaction or provider-specific behaviour;
- require HTTP authorization evidence for changed authorization/tenant boundaries;
- require Playwright/browser evidence for changed user-visible critical flows, including narrow viewport evidence where mobile behaviour changed;
- require plan/what-if or equivalent infrastructure evidence for deployment-definition changes;
- verify migration ordering, compatibility windows, temporary triggers/workarounds and rollback dependencies explicitly;
- record any explicit exception with owner, risk and scope before promotion.

A release CI run proves that the combined source builds and passes the deterministic repository suite. It does **not** retroactively prove an unexecuted integration, relational, browser, infrastructure or deployed check from an included change.

## Test selection

Add regression tests for behaviour with meaningful breakage risk:

- business rules, calculations and branching workflows;
- authorization and tenant isolation;
- state transitions and concurrency;
- transactions, rollback, retries, idempotency and partial failure;
- relational constraints/query behaviour;
- external integration boundaries;
- critical edge cases and verified bugs.

Do not add tests for trivial getters, simple mappings, framework behaviour or CRUD pass-through solely to increase coverage.

Use a relational provider when SQL translation, constraints, transactions, cascade behaviour or concurrency matter. EF Core in-memory tests do not prove those properties.

## Playwright

A user-visible frontend change is not browser-validated until Playwright operates the changed control in a running app.

The relevant flow should:

- navigate as the user would;
- interact with the changed controls rather than only load the page;
- verify visible/persisted result and important loading/error/recovery states;
- inspect console and failed/duplicated network requests where relevant;
- exercise auth, redirect, browser-back, logout or cache isolation when those behaviours changed;
- include a narrow viewport for mobile-sensitive changes;
- use synthetic/non-production data and keep credentials/personal data out of artifacts.

If Playwright cannot run, report **implemented but Playwright-unvalidated**, name the missing prerequisite and keep the PR draft/blocked unless an explicit exception is approved.

Reusable critical-flow details belong in [`../operations/playwright-critical-flows.md`](../operations/playwright-critical-flows.md), not duplicated here.

## Backend and integrations

Validate the boundary that can actually fail:

- call HTTP endpoints when route/auth/error contracts changed;
- use relational tests when persistence semantics changed;
- verify two-tenant behaviour when tenant isolation is at risk;
- cover retry/cancellation/conflict/partial completion when side effects make them material;
- use fakes for destructive external operations, followed by safe isolated smoke only when useful and approved.

A successful external call does not prove persistence succeeded, and a committed database transaction does not prove an external side effect succeeded. Test the failure direction the design claims to handle.

## Infrastructure

Template compilation proves syntax, not deployment. Deployment proves resource creation, not application behaviour.

Validate the smallest relevant chain: template/script syntax → plan/what-if → deployment when explicitly in scope → affected runtime smoke.

Do not perform destructive production validation just to produce evidence.

## Compliance-sensitive changes

Do not duplicate GDPR/AI checklists here. When a change affects personal-data processing, retention/deletion/rights, telemetry containing personal data, a processor/international transfer, or an AI system, apply the current gate in [`../compliance/GDPR_AI_ACT_BASELINE.md`](../compliance/GDPR_AI_ACT_BASELINE.md).

Functional test success is not proof of legal compliance. Report engineering evidence and accountable/legal approval separately.

## Evidence to report

For implementation work, state only the categories that apply:

- static review performed;
- exact build/lint/typecheck/docs commands run;
- focused automated tests and result;
- integration scenarios run;
- Playwright scenarios, browser/viewport and result;
- deployed smoke performed;
- compliance/operational evidence or approvals updated;
- required validation not run, with the exact reason.

Avoid generic “tests passed” or “validated” claims when they hide which level actually ran.
