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

The pull-request `CI Gate` owns deterministic repository validation before merge:

- backend Release build and merge-critical backend regression tests;
- frontend no-new-lint regression checking;
- branch-matched OpenAPI/Orval generation;
- frontend tests and production build; and
- repository contract/documentation checks.

The inherited backend full-suite failures exposed during this cutover are tracked by WOR-382. The full suite is temporarily retained as visible inventory and must become blocking when that debt is removed.

Code scanning has one owner: GitHub CodeQL Default setup. The normal CI workflow must not add an advanced CodeQL configuration while Default setup is enabled. Whether code-scanning results block merge is repository security/ruleset configuration, not a second CI implementation.

A separate `release/**` candidate branch is not used for normal production delivery.

Vercel may deploy the merged frontend from `main`. Backend deployment starts only after the unified CI workflow has successfully revalidated the exact `main` SHA, and Azure deployment continues to use the `prod` environment, OIDC, bounded retries and health verification.

Production infrastructure reconciliation and risk-based deployed Playwright scenarios remain separate operational workflows because they have different privileges and failure semantics from normal application delivery.

GitHub tags/releases may mark meaningful product versions, but they do not form another deployment gate.

## Consequences

### Positive

- One branch has one meaning: merged `main` is production code.
- Manual merge is the explicit release decision.
- Validation moves before the production decision instead of behind a second branch.
- Frontend and backend share the same code boundary.
- Issue-specific, release-branch-only and duplicate code-scanning automation can be removed.
- CI failures become actionable merge blockers instead of background release ceremony.

### Trade-offs

- The `main` repository ruleset is a critical control and must require `CI Gate`, pull requests and no direct/force pushes.
- Vercel can start the frontend deployment immediately after merge, so the production frontend build must remain in pull-request CI.
- Backend is intentionally more conservative: it waits for a successful post-merge CI run for the exact `main` SHA before Azure deployment.
- The backend full test suite cannot become a trustworthy blocking gate until the inherited failures recorded in WOR-382 are removed; the temporary inventory step must not become permanent.
- Large or unusually risky changes can still require additional Playwright, infrastructure or operational evidence; the simplified branch model does not reduce risk-based testing requirements.
