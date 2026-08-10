# ADR 0005: Use `main` as the production boundary

**Status:** Accepted  
**Date:** 2026-08-08  
**Amended:** 2026-08-10  
**Decision owners:** Workslip maintainers

## Context

Workslip previously mixed release-candidate branches and production deployment boundaries in ways that made frontend/backend delivery difficult to reason about. The production property that must remain stable is that only code explicitly promoted to `main` can trigger application production deployment.

The team now also wants a release integration branch where multiple reviewed changes can be accumulated and stabilized before one explicit promotion to `main`.

These are separate concerns:

- `release-*` is an integration and release-candidate boundary;
- `main` is the production code boundary.

The CI and repository protection model must preserve that distinction. A release branch must receive the same deterministic merge validation as normal production-bound work, but a release-branch CI run must never trigger production deployment by itself.

## Decision

`main` remains the single application production boundary.

Normal delivery is now:

`rbj--<issue>-...` → pull request → active `release-*` branch → release stabilization → pull request to `main` → production.

For release 4.0.1, the active integration branch is `release-4.0.1`.

### Feature and fix pull requests

Feature/fix pull requests target the active `release-*` branch and must pass the unified `CI Gate` before merge.

The pull-request `CI Gate` owns deterministic repository validation:

- full backend Release build and test suite;
- frontend no-new-lint regression checking;
- branch-matched OpenAPI/Orval generation;
- frontend tests and production build; and
- repository contract/documentation checks.

The same CI also runs after pushes to `release-*` so the integrated release candidate is continuously revalidated.

### Promotion to production

A release reaches production only through an explicit pull request from the active release branch to `main`.

`main` must not accept direct pushes. Its repository ruleset must require a pull request, block force pushes and require the expected validation before merge.

After the release PR merges, CI runs again on the exact `main` SHA. Backend production deployment remains gated on a successful `CI` workflow run caused by a push to `main`. Release-branch CI is not a deployment trigger.

Vercel production remains tied to `main`; a release branch is not a frontend production branch.

### CI concurrency

Pull-request CI is disposable and may cancel an in-progress run when the same PR receives a newer commit.

Push CI on `release-*` and `main` is integration evidence and should be allowed to finish. In particular, an already-running `main` CI must not be canceled by a later merge because its successful completion is a backend production deployment dependency.

### Code scanning and operational workflows

GitHub CodeQL Default setup remains the code-scanning owner. The normal CI workflow must not introduce a duplicate advanced CodeQL setup while Default setup is active.

Production infrastructure reconciliation and risk-based deployed Playwright scenarios remain separate operational workflows because they have different privileges and failure semantics from normal application delivery.

GitHub tags/releases may mark meaningful product versions, but they do not form another deployment gate.

## Consequences

### Positive

- `release-*` has one clear meaning: reviewed integration candidate, not production.
- `main` keeps one clear meaning: production code boundary.
- Multiple changes can be tested together before promotion without allowing release branches to deploy production directly.
- Feature PRs and release promotion both remain explicit human merge decisions.
- Backend and frontend continue to share the same production code boundary.

### Trade-offs

- Release branches add one intentional promotion step compared with direct feature-to-main delivery.
- Repository rulesets become critical controls on both the active release branch and `main`.
- Branch ancestry can make dependent PR diffs temporarily include earlier release work; merge order must remain explicit where real dependencies exist.
- CI must cover `release-*` while production deployment workflows must remain narrowly scoped to `main`.
- Large or unusually risky releases can still require additional Playwright, infrastructure or operational evidence; the release branch does not reduce risk-based validation requirements.

## Required repository protection

The intended GitHub configuration is:

### `main`

- pull request required;
- direct pushes blocked, including administrators unless an explicit emergency bypass policy is documented;
- force pushes blocked;
- required validation checks enforced before merge;
- merge remains an explicit human action.

### active `release-*`

- pull request required for normal feature/fix delivery;
- `CI Gate` required;
- direct/force pushes blocked for normal development;
- emergency bypasses, if any, must be explicit and auditable.
