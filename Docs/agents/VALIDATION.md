# Workslip testing and validation contract

**Status:** Active
**Owner:** Workslip maintainers
**Source of truth:** Executed commands, test output, browser evidence, workflow results, and deployed smoke observations
**Review cadence:** When test infrastructure, CI, or critical user flows change

## Validation truth

Code inspection is mandatory, but code inspection is not testing. Compilation proves only that code can be built. Automated tests prove only the behavior they actually execute. UI behavior is not considered validated until Playwright has exercised the changed flow in a running application.

Unexecuted tests are evidence of intended coverage, not evidence that the feature works.

## Validation ladder

Report each level independently:

1. **Static review** — inspect source, diff, contracts, configuration, security boundaries, and failure paths.
2. **Compilation and static tooling** — restore dependencies, build Release artifacts, run lint, TypeScript, analyzers, and generated-artifact checks.
3. **Automated behavioral tests** — run focused unit, service, authorization, persistence, concurrency, and regression tests.
4. **Integration validation** — run the API and exercise HTTP contracts, relational database behavior, authentication, and safe external integration boundaries.
5. **Playwright validation** — start the required application services and use Playwright to operate the actual changed controls in a real browser.
6. **Deployed smoke validation** — after deployment when in scope, verify the critical path in the target environment without destructive production testing.

Skipping a lower level requires an explicit reason. Passing a higher level does not automatically replace a relevant lower-level check.

## Minimum validation by change type

| Change type | Required minimum |
|---|---|
| Documentation-only | Static review and documentation tooling when available |
| Backend business logic | Release build and focused behavioral tests |
| Authorization or tenant boundary | Release build, focused authorization/tenant tests, and HTTP integration |
| EF Core or schema behavior | Release build, relational-database tests, and production-data impact review |
| API contract | Release build, focused endpoint tests, OpenAPI/client consistency, and HTTP smoke |
| Background service | Release build, focused lifecycle/failure tests, and hosted-process smoke where available |
| Frontend code with no visible behavior change | Lint, TypeScript, production build, and focused automated tests where useful |
| Any user-visible frontend change | Lint, TypeScript, production build, and Playwright against a running app |
| Routing, forms, authentication, session, cache, or critical workflow | Full Playwright success, failure, recovery, redirect, console, and network validation |
| External integration | Contract/fake tests plus an isolated real smoke when safe and authorized |
| Infrastructure | Syntax/template validation and plan/what-if; deployment validation only when explicitly in scope |

## Playwright is mandatory for user-visible frontend work

For any change that affects what a user sees or does, Playwright must be involved. Reading React code, running TypeScript, rendering a screenshot, or checking a route manually in source is insufficient.

A Playwright validation must:

- start or target a runnable frontend and the required backend;
- authenticate with an appropriate non-production account when authentication is involved;
- navigate through the same route a user would use;
- click the actual changed buttons, links, fields, dialogs, and controls;
- submit real forms and verify the resulting visible state;
- verify relevant loading, disabled, empty, success, error, and recovery states;
- inspect browser console errors and unhandled exceptions;
- inspect failed, duplicated, canceled, or incorrectly scoped network requests;
- verify redirects, browser-back behavior, session recovery, and cache isolation when relevant;
- use at least one narrow mobile viewport for mobile-sensitive changes;
- avoid destructive production actions.

A browser test that loads a page but never interacts with the changed control does not validate the feature.

## Merge-readiness rule for frontend changes

A user-visible frontend PR is not considered validation-complete or merge-ready until the relevant Playwright flow has passed.

When Playwright cannot run because the application, test identity, environment, or browser tooling is unavailable:

- report the change as **implemented but Playwright-unvalidated**;
- list the exact missing prerequisite;
- provide the exact flow that remains to be executed;
- keep the PR draft or explicitly mark it blocked on browser validation;
- do not ask the product owner to perform routine validation that the agent can perform once the environment exists.

Emergency exceptions require explicit product-owner approval and a follow-up Linear issue with an owner and due date.

## Playwright evidence

Record enough evidence to reproduce the result:

- branch or commit SHA;
- environment and base URL;
- browser and viewport;
- account role and organization context without exposing credentials;
- scenario names and outcomes;
- console/network failures found;
- screenshots, traces, or videos for failures and high-risk flows when useful.

Do not commit secrets, authenticated storage state, access tokens, or personal data in Playwright artifacts.

## Backend and integration testing

Backend validation should use the real dependency type or the closest isolated equivalent:

- use a relational provider for EF Core query, transaction, constraint, and concurrency behavior;
- call the actual HTTP endpoint for API contracts and authorization boundaries;
- verify tenant isolation with at least two organizations where relevant;
- verify unauthorized, forbidden, missing, conflict, retry, and cancellation paths where they carry risk;
- exercise background-service failure and restart behavior when changed;
- use fakes for destructive external operations, followed by a safe isolated real smoke when authorized.

The EF Core in-memory provider must not be treated as proof of SQL translation, relational constraints, transaction behavior, or production concurrency.

## Test selection

Add tests where they provide meaningful regression protection:

- business rules and calculations;
- branching workflows and state transitions;
- authorization and tenant isolation;
- transactions, rollback, retries, and idempotency;
- concurrency;
- external integration boundaries;
- critical edge cases and verified regressions.

Do not add tests for trivial getters, simple mappings, framework behavior, basic CRUD pass-through, or implementation details without concrete risk. Do not optimize for coverage percentage.

## Validation report format

Every implementation report and PR description must state:

- what was statically reviewed;
- exact build/lint/typecheck commands that ran;
- exact automated tests that ran and their outcome;
- integration scenarios that ran;
- Playwright scenarios that ran, including browser and viewport;
- deployed smoke checks that ran;
- anything required but not executed, with the precise reason.

Do not collapse these into a generic “tests passed” statement.
