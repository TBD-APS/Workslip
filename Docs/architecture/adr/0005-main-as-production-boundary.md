# ADR 0005: Use `main` as the production boundary

**Status:** Accepted  
**Date:** 2026-08-08  
**Decision owners:** Workslip maintainers

## Context

Workslip had two production boundaries.

The Vercel frontend already treated a merge to `main` as a production deployment, while the backend required a second `release/**` branch, a separate release-validation workflow and an explicit branch/SHA handoff before Azure deployment.

That split added operational ceremony and duplicated validation without preventing frontend code from reaching production first. It also made the meaning of `main`, release branches and GitHub Actions difficult to explain and easy to operate inconsistently.

The desired operating property is simpler: the code that is explicitly approved and merged is the code being released, while strong validation happens before that release decision.

A later production incident exposed a second-order requirement in this model: Azure deployment waits for a successful completed `main` CI run. If each subsequent merge cancels the already-running `main` CI, a high merge rate can prevent any run from ever reaching the successful completion event that releases the backend. Vercel continues advancing from `main` in that situation, creating frontend/backend deployment drift even though every individual PR was green.

## Decision

`main` is the single application production boundary.

Normal delivery is:

`rbj--<issue>-...` → pull request → `CI Gate` → explicit manual merge → `main` → production.

The pull-request `CI Gate` owns deterministic repository validation before merge:

- full backend Release build and test suite;
- frontend no-new-lint regression checking;
- branch-matched OpenAPI/Orval generation;
- frontend tests and production build; and
- repository contract/documentation checks.

Code scanning has one owner: GitHub CodeQL Default setup. The normal CI workflow must not add an advanced CodeQL configuration while Default setup is enabled. Whether code-scanning results block merge is repository security/ruleset configuration, not a second CI implementation.

A separate `release/**` candidate branch is not used for normal production delivery.

Vercel may deploy the merged frontend from `main`. Backend deployment starts only after the unified CI workflow has successfully revalidated the exact `main` SHA, and Azure deployment continues to use the `prod` environment, OIDC, bounded retries and health verification.

CI concurrency must preserve that release path:

- pull-request CI is disposable and may cancel an in-progress run when the same PR receives a newer commit;
- an already-running `main` push CI must not be canceled by a later merge, because its successful completion is a production dependency;
- `main` pushes remain in one CI concurrency group so GitHub can coalesce superseded pending runs to the newest pending revision while allowing the active run to finish and release backend delivery.

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
- Frequent merges cannot repeatedly cancel the only main CI run capable of triggering Azure backend deployment.

### Trade-offs

- The `main` repository ruleset is a critical control and must require `CI Gate`, pull requests and no direct/force pushes.
- Vercel can start the frontend deployment immediately after merge, so the production frontend build must remain in pull-request CI.
- Backend is intentionally more conservative: it waits for a successful post-merge CI run for the exact `main` SHA before Azure deployment.
- During a burst of merges, one active main CI run continues while superseded pending main runs may be replaced by a newer pending revision. This spends some CI time on an older accepted `main` SHA in exchange for guaranteeing forward progress of backend delivery.
- Large or unusually risky changes can still require additional Playwright, infrastructure or operational evidence; the simplified branch model does not reduce risk-based testing requirements.
