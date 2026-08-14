# Workslip validation rules

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** executed commands, test output, browser evidence, workflow results and deployed/operational evidence  
**Review cadence:** When validation tooling or release gates change

Validation should answer one question: **what evidence is needed to make the changed risk believable?**

Do not optimize for test count or coverage percentage. Use the smallest evidence set that proves the real regression risk.

Engineering gates and product regression evidence are different things. Build, lint, typecheck, OpenAPI generation/consistency, migration/schema validation and documentation checks can be mandatory without justifying more product tests.

## Default regression-test tools

Workslip uses three default regression-test tools:

1. **Unit** — business rules, calculations, important state transitions and deterministic edge cases.
2. **Postman feature/API** — primary backend feature verification across HTTP, authorization, persistence and coherent multi-endpoint workflows.
3. **Playwright** — critical changed user-visible browser flows.

Use another test shape only when one of these cannot prove a concrete risk efficiently, for example a narrow relational/provider-specific test for SQL translation, constraints, transaction semantics or concurrency.

## Selection rule

Before adding a test, name the production failure it protects against. Then choose the cheapest strong evidence.

| Risk | Preferred evidence |
|---|---|
| Pure calculation or branching business rule | Unit |
| Important deterministic state transition | Unit |
| Backend feature through public/internal HTTP contract | Postman feature/API |
| Authorization, role or tenant boundary exposed through API | Postman feature/API, plus a narrow Unit policy test only when it adds distinct value |
| Multi-endpoint workflow with persisted result | Postman feature/API |
| User-visible critical changed journey | Playwright |
| Browser routing/session/cache/mobile behavior | Playwright |
| SQL/provider behavior not reliably observable through the API | Narrow relational/provider-specific test |
| External integration failure with material side effects | Focused boundary test or safe isolated smoke, only for the failure direction the design claims to handle |

If there is no meaningful regression risk, do not add a test solely because code changed.

## Unit tests

Use Unit tests for logic that is clearer and cheaper to prove without HTTP or a browser:

- calculations and rounding;
- meaningful business branching;
- important state transitions;
- deterministic edge cases;
- isolated authorization/domain policies where a policy-level test gives value beyond the API flow.

Do not add Unit tests for:

- trivial getters/setters;
- simple mappings;
- framework behavior;
- CRUD pass-through;
- implementation details with no credible regression risk;
- mocked end-to-end flows that a Postman feature test proves more directly.

## Postman feature/API tests

Postman is the primary backend feature boundary.

Prefer one coherent feature flow over many microscopic endpoint tests. A useful flow normally contains only the steps needed to prove the risk:

1. establish synthetic non-production context and authentication;
2. perform the feature action through the API;
3. assert the response contract;
4. read back or otherwise prove the persisted/state result when relevant;
5. exercise the highest-value authorization, tenant or error path when that is part of the changed risk.

Use Postman for:

- request → service → persistence → response behavior;
- authorization and permission boundaries;
- tenant-isolation probes;
- validation, not-found and conflict behavior when materially changed;
- workflows spanning multiple endpoints;
- API contract regressions that need real HTTP evidence.

Runtime evidence requires the collection/scripts to actually execute against an approved localhost, test or staging target. Parsing `postman_collection.json`, syntax-checking scripts or reading assertions is **not** runtime API evidence.

The active runner and environment rules live in [`../../src/BE/WorkslipApi/Postman/README.md`](../../src/BE/WorkslipApi/Postman/README.md). If an authenticated safe target is unavailable, report the missing Postman runtime evidence explicitly instead of marking it passed.

Do not run mutation-heavy feature suites against ordinary production data.

## Playwright

Use Playwright when the regression lives in the browser experience rather than merely because frontend files changed.

Use it for:

- critical changed user journeys;
- auth, redirect, browser-back, session or cache behavior;
- important loading, error and recovery behavior;
- mobile-sensitive interaction when the changed behavior is mobile-sensitive;
- UI behavior where persisted/visible state must be proven through the actual controls.

A relevant flow should navigate and interact as the user would and verify the important visible or persisted result. Inspect console or failed/duplicated network requests when they are part of the risk.

Do not run a broad Playwright suite solely because a frontend component changed. Keep the scenario focused on the changed critical journey.

Use synthetic/non-production data and keep credentials, OTC values, tokens and personal data out of artifacts.

If required Playwright cannot run, report **implemented but Playwright-unvalidated**, name the missing prerequisite and keep the PR blocked/draft unless an explicit exception is approved.

Reusable critical-flow details belong in [`../operations/playwright-critical-flows.md`](../operations/playwright-critical-flows.md), not duplicated here.

## Engineering gates

These checks are repository/engineering gates. Run the ones required by the changed surface, but do not convert each gate into another regression-test layer.

1. **Static review** — inspect source, diff, configuration, contracts, boundaries and failure paths.
2. **Build/static tooling** — restore/build, lint/typecheck, analyzers and generated-artifact checks.
3. **Contract/schema checks** — OpenAPI/client consistency, migration validation, schema/provider checks when changed.
4. **Documentation checks** — `python tools/docs/check_docs.py` for maintained docs.
5. **Deployed smoke** — non-destructive verification in the target environment when deployment is in scope.
6. **Operational/compliance evidence** — only when the change affects data lifecycle, processors, legal controls or AI governance.

Compilation proves compilation. A JSON parse proves syntax. A deployed resource proves creation. Report each claim as the evidence it actually provides.

## Minimum by change type

| Change | Minimum evidence |
|---|---|
| Documentation only | source review + `python tools/docs/check_docs.py` |
| Backend business rule without HTTP/persistence behavior change | Release build + focused Unit tests when the rule has meaningful regression risk |
| Backend feature / API behavior | Release build + relevant Postman feature/API flow; OpenAPI/client consistency when the contract changed |
| Authorization / tenant boundary | Release build + Postman authorization/tenant flow; narrow policy Unit test only when useful |
| EF Core/schema/transaction | Release build + migration/schema review + narrow relational/provider-specific test only when the risky behavior cannot be proven adequately through the API |
| Background service | Release build + focused lifecycle/failure test only for material behavior; hosted smoke when useful |
| Frontend, no visible behavior change | lint/typecheck as applicable + production build; Unit only for meaningful frontend logic risk |
| User-visible critical frontend flow | lint/typecheck as applicable + production build + relevant Playwright flow |
| Auth/routing/forms/session/cache critical flow | relevant Playwright success/failure/recovery path |
| Infrastructure | syntax/template validation + plan/what-if; deployment smoke only when deployment is in scope |
| External integration | focused boundary/feature flow + retry/partial-failure review; safe isolated smoke only when justified |
| Personal data / processor / AI system | this document **plus** [`../compliance/GDPR_AI_ACT_BASELINE.md`](../compliance/GDPR_AI_ACT_BASELINE.md) |

The matrix is a floor for evidence, not a checklist to maximize.

## Backend and integration failure directions

A successful external call does not prove persistence succeeded, and a committed database transaction does not prove an external side effect succeeded.

When partial failure, retry, cancellation, conflict, concurrency or idempotency is a material feature risk, test the specific failure direction the design claims to handle. Do not create a generic failure matrix for every endpoint.

Use a relational provider only when SQL translation, constraints, transaction behavior, cascade behavior or concurrency is the actual thing being proven. EF Core in-memory tests do not prove those properties.

## Infrastructure

Template compilation proves syntax, not deployment. Deployment proves resource creation, not application behavior.

Validate the smallest relevant chain: template/script syntax → plan/what-if → deployment when explicitly in scope → affected runtime smoke.

Do not perform destructive production validation just to produce evidence.

## Compliance-sensitive changes

Do not duplicate GDPR/AI checklists here. When a change affects personal-data processing, retention/deletion/rights, telemetry containing personal data, a processor/international transfer, or an AI system, apply the current gate in [`../compliance/GDPR_AI_ACT_BASELINE.md`](../compliance/GDPR_AI_ACT_BASELINE.md).

Functional test success is not proof of legal compliance. Report engineering evidence and accountable/legal approval separately.

## Evidence to report

For implementation work, state only the categories that apply:

- static review performed;
- exact build/lint/typecheck/docs commands run;
- Unit scenarios run and result;
- Postman feature/API scenarios actually executed and target class (localhost/test/staging), or the exact missing prerequisite;
- Playwright scenarios, browser/viewport and result, or the exact missing prerequisite;
- narrow relational/provider-specific evidence when it was genuinely needed;
- deployed smoke performed;
- compliance/operational evidence or approvals updated;
- required validation not run, with the exact reason.

Avoid generic “tests passed” or “validated” claims when they hide which evidence actually ran.
