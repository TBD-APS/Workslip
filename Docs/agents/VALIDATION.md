# Workslip testing and validation contract

**Status:** Active
**Owner:** Workslip maintainers
**Source of truth:** Executed commands, test output, browser evidence, workflow results, deployed smoke observations, approved compliance records, and operational evidence
**Review cadence:** When test infrastructure, CI, critical user flows, personal-data processing, or AI-system use changes

## Validation truth

Code inspection is mandatory, but code inspection is not testing. Compilation proves only that code can be built. Automated tests prove only the behavior they actually execute. UI behavior is not considered validated until Playwright has exercised the changed flow in a running application.

A privacy-friendly implementation is not proof of GDPR compliance. An AI test result is not proof of AI Act compliance. Legal roles, lawful basis, contracts, retention, rights procedures, DPIAs, transfer assessments, training, transparency, human oversight, and operational controls require their own evidence and accountable approval.

Unexecuted tests are evidence of intended coverage, not evidence that the feature works.

## Validation ladder

Report each level independently:

1. **Static review** — inspect source, diff, contracts, configuration, security/privacy boundaries, dataflows, AI classification, and failure paths.
2. **Compilation and static tooling** — restore dependencies, build Release artifacts, run lint, TypeScript, analyzers, generated-artifact checks, secret/personal-data scans, and documentation checks.
3. **Automated behavioral tests** — run focused unit, service, authorization, persistence, concurrency, lifecycle, retention, deletion, redaction, AI-safety, and regression tests.
4. **Integration validation** — run the API and exercise HTTP contracts, relational database behavior, authentication, safe external integration boundaries, data locations, processor failure, and cross-tenant behavior.
5. **Playwright validation** — start the required application services and use Playwright to operate the actual changed controls in a real browser.
6. **Deployed smoke validation** — after deployment when in scope, verify the critical path in the target environment without destructive production testing.
7. **Operational/compliance evidence** — verify retention jobs, rights procedures, deletion outcomes, vendor settings/contracts, transfer controls, incident readiness, AI inventory/classification, training, monitoring, and accountable approvals where applicable.

Skipping a lower level requires an explicit reason. Passing a higher level does not automatically replace a relevant lower-level check. Functional success does not replace privacy, security, legal, or AI-governance evidence.

## Minimum validation by change type

| Change type | Required minimum |
|---|---|
| Documentation-only | Static review, source/date verification for legal claims, and documentation tooling when available |
| Backend business logic | Release build and focused behavioral tests |
| Authorization or tenant boundary | Release build, focused authorization/tenant tests, and HTTP integration |
| EF Core or schema behavior | Release build, relational-database tests, production-data impact review, and lifecycle/retention review |
| API contract | Release build, focused endpoint tests, OpenAPI/client consistency, and HTTP smoke |
| Background service | Release build, focused lifecycle/failure tests, and hosted-process smoke where available |
| Frontend code with no visible behavior change | Lint, TypeScript, production build, and focused automated tests where useful |
| Any user-visible frontend change | Lint, TypeScript, production build, and Playwright against a running app |
| Routing, forms, authentication, session, cache, or critical workflow | Full Playwright success, failure, recovery, redirect, console, network, logout, and cache-isolation validation |
| External integration or processor | Contract/fake tests, isolated real smoke when safe, failure/retry/deletion review, data-location/retention evidence, and vendor approval |
| Personal-data collection or lifecycle | GDPR change-gate evidence, authorization/tenant tests, minimization review, rights/retention/deletion tests, log/artifact redaction, and operational evidence |
| Telemetry, logs, analytics, or monitoring | Field/cardinality review, personal-data and secret redaction tests, access/retention review, opt-in/legal-basis ownership, and deployed payload inspection |
| Data export, import, deletion, anonymization, or retention | Relational integration, authorization, completeness, idempotency, partial-failure, retry, file/cache/vendor propagation, and auditable outcome evidence |
| AI system or AI-assisted feature | AI inventory/role/risk evidence, prohibited-practice screening, data/vendor review, functional and adversarial evaluation, transparency/human-oversight validation, monitoring and rollback evidence |
| Infrastructure | Syntax/template validation, plan/what-if, region/identity/encryption/logging/backup/compliance review; deployment validation only when explicitly in scope |

## Playwright is mandatory for user-visible frontend work

For any change that affects what a user sees or does, Playwright must be involved. Reading React code, running TypeScript, rendering a screenshot, or checking a route manually in source is insufficient.

A Playwright validation must:

- start or target a runnable frontend and the required backend;
- authenticate with an appropriate non-production account when authentication is involved;
- navigate through the same route a user would use;
- click the actual changed buttons, links, fields, dialogs, notices, choices, and controls;
- submit real forms and verify the resulting visible and persisted state;
- verify relevant loading, disabled, empty, success, error, privacy, transparency, and recovery states;
- inspect browser console errors and unhandled exceptions;
- inspect failed, duplicated, canceled, incorrectly scoped, or privacy-leaking network requests;
- verify redirects, browser-back behavior, session recovery, logout, role/tenant changes, and cache isolation when relevant;
- use at least one narrow mobile viewport for mobile-sensitive changes;
- avoid destructive production actions and production personal data;
- ensure screenshots, videos, traces, reports, and downloaded files contain no credentials, tokens, unnecessary personal data, or cross-tenant content.

A browser test that loads a page but never interacts with the changed control does not validate the feature. A screenshot of personal data is not acceptable evidence unless created with approved synthetic data and restricted appropriately.

## Merge-readiness rule for frontend changes

A user-visible frontend PR is not considered validation-complete or merge-ready until the relevant Playwright flow has passed.

When Playwright cannot run because the application, test identity, environment, browser tooling, synthetic data, vendor sandbox, or required compliance approval is unavailable:

- report the change as **implemented but Playwright-unvalidated**;
- list the exact missing prerequisite;
- provide the exact flow that remains to be executed;
- keep the PR draft or explicitly mark it blocked on browser validation;
- do not ask the product owner to perform routine validation that the agent can perform once the environment exists.

Emergency exceptions require explicit product-owner approval and a follow-up Linear issue with an owner and due date. A compliance release blocker in `Docs/compliance/GDPR_AI_ACT_BASELINE.md` cannot be waived merely because a deadline is inconvenient.

## Playwright evidence

Record enough evidence to reproduce the result:

- branch or commit SHA;
- environment and base URL;
- browser and viewport;
- synthetic account role and organization context without exposing credentials or production identifiers;
- scenario names and outcomes;
- console/network failures found;
- privacy/transparency notices, choices, human-review paths, and data lifecycle actions exercised;
- screenshots, traces, or videos for failures and high-risk flows when useful and safely redacted;
- artifact retention and access scope.

Do not commit secrets, authenticated storage state, access tokens, production data, real customer details, rights-request content, incident data, AI prompts containing confidential data, or unnecessary personal data in Playwright artifacts.

## GDPR and personal-data validation

Apply the current gate in [`../compliance/GDPR_AI_ACT_BASELINE.md`](../compliance/GDPR_AI_ACT_BASELINE.md).

### Data minimization and purpose

Verify that:

- each new field is required for the approved purpose;
- optional data is not collected, displayed, logged, or retained by default;
- API responses do not over-return personal data;
- search, filters, autocomplete, exports, PDFs, notifications, telemetry, and caches expose only the required fields and audience;
- test and demo fixtures are synthetic and clearly separated from production.

### Authorization, isolation, and revocation

Verify with at least two tenants and relevant roles that:

- direct identifiers and guessed IDs cannot cross tenant boundaries;
- list, search, export, file, PDF, history, notification, and cache paths enforce the same boundary;
- removed users, role changes, organization switches, logout, token expiry, and permission revocation take effect without stale cached access;
- background jobs, integrations, and cleanup tasks preserve tenant scope.

### Rights and lifecycle

Where applicable, exercise:

- access and export completeness in a structured format;
- rectification and propagation to current views without falsifying immutable history;
- erasure/anonymization across relational rows, files, derived data, caches, search, telemetry where identifiable, and downstream processors;
- restriction and objection controls;
- portability semantics;
- tenant termination and user removal;
- retention expiration, legal-hold exceptions, retry, partial failure, idempotency, and auditable completion;
- backup expiry/restore treatment through documented operational evidence when it cannot be safely exercised in automated tests.

A successful HTTP delete is not sufficient if linked, derived, cached, filed, telemetered, or processor-held data remains undocumented.

### Logs, telemetry, and artifacts

Inspect actual emitted payloads where safe and verify:

- no tokens, secrets, passwords, raw authorization headers, personal data, customer content, addresses, free text, or production identifiers leak unexpectedly;
- route and dependency names remove query values and entity identifiers;
- metrics have bounded cardinality and do not use personal or tenant identifiers as dimensions;
- exception handling does not serialize request/response bodies;
- retention, access control, region, sampling, and deletion settings match the approved record;
- CI, screenshots, traces, reports, dumps, support bundles, and source maps do not expose restricted data.

### Vendors and transfers

For a processor/integration change, validation evidence must include:

- exact deployed endpoint/region and account settings;
- data sent and received, including hidden metadata and vendor logs;
- retention/deletion/training/secondary-use settings;
- failure, retry, timeout, cancellation, duplication, and termination behavior;
- approved contract, subprocessor, and transfer record reference without copying restricted terms into the public repository.

## EU AI Act and AI-system validation

No AI test may run with production personal data, credentials, confidential customer content, or unrestricted external tool access unless explicitly approved under the compliance baseline.

### Required evidence

For each AI system or AI-assisted capability, record:

- inventory identifier, provider/model/version, purpose, users, affected people, inputs/outputs, tools, retrieval sources, and deployment region;
- Workslip's legal role and current AI Act classification, with official source and review date;
- prohibited-practice screening and required transparency, human oversight, logging, and quality controls;
- training-data or grounding-data provenance, quality, representativeness, licensing, and personal-data handling where applicable;
- provider terms for retention, training, human access, model improvement, security, subprocessors, regions, transfers, incident notification, and version changes;
- AI literacy and operator instructions.

### Functional and quality evaluation

Use an approved, versioned evaluation set and measure behavior relevant to the use case, including:

- task success, accuracy, false positives/negatives, uncertainty, abstention, and reproducibility;
- subgroup or context performance where bias or unequal impact is plausible;
- fabricated, misleading, discriminatory, harmful, illegal, or unsafe outputs;
- language, accessibility, and Danish-domain performance where relevant;
- human ability to understand limitations, identify AI involvement, review evidence, override, correct, stop, and escalate;
- non-AI fallback and provider outage behavior.

Do not reduce AI evaluation to a few manually selected prompts.

### Adversarial and security evaluation

Test as applicable:

- direct and indirect prompt injection;
- retrieval poisoning and unauthorized context retrieval;
- cross-tenant prompt, cache, vector, memory, file, and conversation leakage;
- secret and personal-data exfiltration;
- insecure tool invocation, excessive agency, privilege escalation, and unsafe actions;
- malicious files, encoded instructions, data reconstruction, and output injection;
- rate limits, cost exhaustion, denial of service, retry storms, and provider degradation;
- model/version changes and rollback.

### Transparency and significant decisions

Verify that required AI-interaction notices, generated-content labels, explanations, contest routes, and human-review controls are visible, understandable, timely, and not hidden by responsive layouts.

Where an output could influence a legal or similarly significant decision, validation must demonstrate meaningful human intervention rather than nominal approval. The reviewer must receive relevant information, have sufficient time and authority, exercise independent judgment, and be able to change the outcome.

### Production monitoring

Define and verify:

- approved model/version pinning or change-detection process;
- quality, safety, privacy, security, bias, latency, cost, and usage monitoring;
- incident thresholds, user feedback, escalation, containment, rollback, and notification;
- periodic reassessment and evidence retention;
- handling of provider policy, subprocessor, region, model, or terms changes.

A pre-release test pass does not remove post-deployment monitoring obligations.

## Backend and integration testing

Backend validation should use the real dependency type or the closest isolated equivalent:

- use a relational provider for EF Core query, transaction, constraint, lifecycle, deletion, and concurrency behavior;
- call the actual HTTP endpoint for API contracts and authorization boundaries;
- verify tenant isolation with at least two organizations where relevant;
- verify unauthorized, forbidden, missing, conflict, retry, cancellation, and partial-completion paths where they carry risk;
- exercise background-service failure and restart behavior when changed;
- use fakes for destructive external operations, followed by a safe isolated real smoke when authorized;
- verify processor and AI calls through an approved boundary that supports redaction, timeout, retry, cancellation, tenant scope, and auditability.

The EF Core in-memory provider must not be treated as proof of SQL translation, relational constraints, transaction behavior, production concurrency, cascade deletion, or retention behavior.

## Test selection

Add tests where they provide meaningful regression protection:

- business rules and calculations;
- branching workflows and state transitions;
- authorization and tenant isolation;
- personal-data minimization, retention, deletion, export, rights, and redaction;
- transactions, rollback, retries, idempotency, and partial failure;
- concurrency;
- external integration and processor boundaries;
- AI classification-dependent controls, safety, privacy, security, human oversight, and significant edge cases;
- critical edge cases and verified regressions.

Do not add tests for trivial getters, simple mappings, framework behavior, basic CRUD pass-through, or implementation details without concrete risk. Do not optimize for coverage percentage.

## Validation report format

Every implementation report and PR description must state:

- what was statically reviewed;
- the personal-data and AI impact classification;
- exact build/lint/typecheck/documentation/security commands that ran;
- exact automated tests that ran and their outcome;
- integration scenarios that ran;
- Playwright scenarios that ran, including browser and viewport;
- deployed smoke checks that ran;
- privacy lifecycle, telemetry/artifact, vendor, or AI evaluation evidence produced;
- compliance records or accountable approvals updated, referenced by safe identifier;
- anything required but not executed, with the precise reason.

Do not collapse these into a generic “tests passed” or “compliant” statement.
