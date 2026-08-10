# CI quality gates

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** `.github/workflows/`, repository rulesets, Vercel project configuration and current successful runs  
**Review cadence:** When workflows, deployment targets or required checks change

## Principle

Workslip uses a release-integration delivery path:

`rbj--<issue>-...` branch → pull request → `CI Gate` → explicit manual merge → active `release-*` branch → release PR → `main` → production.

`release-*` is an integration/release-candidate boundary. `main` remains the production code boundary.

A workflow should exist only when it provides an actionable signal or performs a required operational task. Do not keep issue-specific, duplicated or routinely ignored automation.

## Pull request and release-branch CI

`.github/workflows/frontend-validation.yml` is the unified `CI` workflow.

It runs for:

- pull requests targeting `main`;
- pull requests targeting `release-*`;
- pushes to `main`; and
- pushes to `release-*`.

This gives feature/fix PRs the same validation before they enter the release candidate, then revalidates the integrated release branch after merges, and finally revalidates the exact `main` production revision after promotion.

The required merge signal is the `CI Gate` job. It succeeds only when these jobs succeed:

- `Backend` — full Release restore, build and backend test suite.
- `Frontend + API contract` — no-new-errors ESLint ratchet, branch-matched OpenAPI/Orval generation, Vitest and production frontend build.
- `Contracts + docs` — production release-policy checks, Playwright source checks, synthetic-auth tests, Postman JSON validation and `python tools/docs/check_docs.py`.

The full backend suite is blocking. Do not replace it with a filtered allowlist, skips or `continue-on-error` to make CI green; repair failing regression tests or production code instead.

The frontend carries inherited ESLint debt. CI compares the current findings with the exact PR base or previous push revision and blocks new severity-2 errors without treating inherited findings as permission to grow the baseline.

The branch-matched frontend client is generated from the backend in the same revision. The shared action is contract generation only; backend tests belong to the `Backend` job so they are not duplicated inside API generation.

## Release integration

Normal feature and fix PRs target the active `release-*` branch, currently `release-4.0.1`.

The release branch is not production. It exists so multiple reviewed changes can be integrated and validated together before one explicit promotion to `main`.

Dependent branches may contain ancestry from earlier work. When such PRs all target the release branch, merge them in dependency order so the later PR diff collapses as its parent work lands.

A release is promoted through a pull request from the active release branch to `main`. That promotion is the final human release decision.

## Code scanning

GitHub CodeQL **Default setup** is the repository's code-scanning owner.

Do not add `github/codeql-action` jobs to the normal CI while Default setup is enabled. GitHub rejects advanced-configuration uploads when Default setup owns the repository, creating duplicate work and red checks rather than additional protection.

Whether code-scanning findings are merge-blocking is repository security/ruleset state and must be verified in GitHub settings. CI workflow YAML must not duplicate that external control.

## Main verification

The same `CI` workflow runs after a release PR merges to `main`.

Core backend, frontend/API-contract and contract/documentation checks run again against the exact production revision. Code scanning remains owned by GitHub Default setup rather than being duplicated in CI.

The post-merge `main` `CI Gate` is the backend deployment trigger. A successful release-branch CI run never triggers production deployment.

Frontend production does not wait for the post-merge GitHub CI run: Vercel production remains configured from `main`. This is why both the release PR and `main` protection remain important controls.

## Production deployment

### Frontend

`src/FE/vercel.json` allows Git production deployment from `main` and disables other branches.

Therefore merging the release PR to `main` is the frontend production release action. A push or merge to `release-*` is not a production deployment action.

### Backend

`.github/workflows/main_api-mrsoftware-prod.yml` listens for a successful `CI` workflow run caused by a push to `main`.

The workflow:

1. records the exact successful `main` SHA;
2. verifies that SHA is still contained in `main`;
3. builds and packages the API from that exact SHA;
4. keeps Azure OIDC permission scoped to the `prod` deployment job;
5. verifies required diagnostics configuration;
6. applies the production release-testing policy;
7. deploys with bounded retries; and
8. requires the API `/health` endpoint to recover.

There is deliberately no deployment trigger from `release-*`.

### Infrastructure and critical-flow testing

Production infrastructure remains a separate manual operation through `.github/workflows/manual-production-infrastructure.yml`.

Deployed Playwright scenarios remain available through `.github/workflows/playwright-prod-smoke.yml`. They are used when the changed risk requires target-environment evidence; they are not a substitute for the pre-merge CI gate.

GitHub Pages remains an independent site/docs deployment concern.

## Repository protection

Repository rulesets must enforce the delivery model, not merely document it.

### `main`

- pull request required;
- direct pushes blocked;
- force pushes blocked;
- required validation checks enforced before merge;
- merge remains an explicit human action.

No normal development commit should be written directly to `main`. Production changes arrive by merging a reviewed release PR.

### active `release-*`

- pull request required for normal feature/fix delivery;
- `CI Gate` required;
- direct pushes blocked for normal development;
- force pushes blocked.

Ruleset configuration is external GitHub state and must be verified after workflow changes. Workflow YAML cannot itself prevent a repository administrator from pushing directly to a branch.

## CI concurrency

Pull-request runs are disposable and may cancel an in-progress run when the same PR receives a newer commit.

Push CI on `release-*` and `main` is allowed to finish. This is particularly important for `main`, because a successful completed `main` run is a backend production deployment dependency.

## Releases and tags

GitHub tags/releases are optional release-history markers for meaningful product versions. They do not control production deployment and should not create another deployment boundary.

## Security

Use GitHub OIDC for Azure deployment. Grant `id-token: write` only to the job that needs the Azure token. Do not introduce publish profiles, long-lived Azure credentials or privileged repository-writing automation without a concrete requirement and reviewed least-privilege design.

The durable decision behind this model is recorded in [`../architecture/adr/0005-main-as-production-boundary.md`](../architecture/adr/0005-main-as-production-boundary.md).
