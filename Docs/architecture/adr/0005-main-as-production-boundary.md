# ADR 0005: Use `main` as the production boundary

**Status:** Accepted  
**Date:** 2026-08-08  
**Decision owners:** Workslip maintainers

## Context

Workslip had two production boundaries.

The Vercel frontend already treated a merge to `main` as a production deployment, while the backend required a second `release/**` branch, a separate release-validation workflow and an explicit branch/SHA handoff before Azure deployment.

That split added operational ceremony and duplicated validation without preventing frontend code from reaching production first. It also made the meaning of `main`, release branches and GitHub Actions difficult to explain and easy to operate inconsistently.

The desired operating property is simpler: the code that is explicitly approved and merged is the code being released, while strong validation happens before that release decision.

## Decision

`main` is the single application production boundary.

Normal delivery is:

`rbj--<issue>-...` → pull request → `CI Gate` → explicit manual merge → `main` → production.

The pull-request `CI Gate` owns the evidence needed before merge, including:

- full backend Release build and tests;
- frontend no-new-lint regression checking;
- branch-matched OpenAPI/Orval generation;
- frontend tests and production build; and
- repository contract/documentation checks.

Stacked Workslip PRs may target another `rbj--...` parent branch so each issue retains a focused diff; the same `CI Gate` runs on those child PRs. Parents are merged first and children are restacked onto the resulting `main` before their own merge.

CodeQL advanced analysis is not an active required CI signal after WOR-382. It was removed when the newly unified gate proved the jobs themselves unstable. Security/static-analysis checks may be reintroduced later only as stable, actionable checks with an explicit ownership model.

A separate `release/**` candidate branch is not used for normal production delivery.

Vercel may deploy the merged frontend from `main`. Backend deployment starts only after the unified CI workflow has successfully revalidated the exact `main` SHA, and Azure deployment continues to use the `prod` environment, OIDC, bounded retries and health verification.

Production infrastructure reconciliation and risk-based deployed Playwright scenarios remain separate operational workflows because they have different privileges and failure semantics from normal application delivery.

GitHub tags/releases may mark meaningful product versions, but they do not form another deployment gate.

## Consequences

### Positive

- One branch has one meaning: merged `main` is production code.
- Manual merge is the explicit release decision.
- Stable build/test/contract evidence moves before the production decision instead of behind a second branch.
- Frontend and backend share the same code boundary.
- Issue-specific and release-branch-only automation can be removed.
- Stacked issue branches can receive the same CI gate without flattening their review diffs.
- CI failures are expected to be actionable merge blockers instead of routinely ignored background signals.

### Trade-offs

- The `main` repository ruleset becomes a critical control and must require `CI Gate`, pull requests and no direct/force pushes.
- Vercel can start the frontend deployment immediately after merge, so pull-request CI must contain the production frontend build and other stable required checks.
- Backend is intentionally more conservative: it waits for a successful post-merge CI run for the exact `main` SHA before Azure deployment.
- CodeQL is not currently part of the required gate; that reduces automated static-analysis coverage until a stable replacement/reintroduction is deliberately chosen.
- Large or unusually risky changes can still require additional Playwright, infrastructure or operational evidence; the simplified branch model does not reduce risk-based testing requirements.