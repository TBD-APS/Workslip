# CI quality gates

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** `.github/workflows/`, repository rulesets, Vercel project configuration and current successful runs  
**Review cadence:** When workflows, deployment targets or required checks change

## Principle

Workslip uses one normal delivery path:

`rbj--<issue>-...` branch → pull request → `CI Gate` → explicit manual merge → `main` → production.

`main` is the production code boundary. A separate `release/**` candidate branch is not part of the active release process.

A workflow should exist only when it provides an actionable signal or performs a required operational task. Do not keep issue-specific, duplicated or routinely ignored automation.

## Pull request CI

`.github/workflows/frontend-validation.yml` is the unified `CI` workflow. It runs for every pull request to `main`, so every change gets the same merge signal rather than a collection of path-specific checks.

The required merge signal is the `CI Gate` job. It succeeds only when these jobs succeed:

- `Backend` — full Release restore, build and backend test suite.
- `Frontend + API contract` — no-new-errors ESLint ratchet, branch-matched OpenAPI/Orval generation, Vitest and production frontend build.
- `Contracts + docs` — production release-policy checks, release-runner and synthetic-auth fail-closed tests, Playwright source checks, Postman JSON validation and `python tools/docs/check_docs.py`.

The full backend suite is blocking. Do not replace it with a filtered allowlist, skips or `continue-on-error` to make CI green; repair failing regression tests or production code instead.

The frontend carries inherited ESLint debt. CI therefore compares the pull-request findings with the exact base revision and blocks new severity-2 errors without treating inherited findings as permission to grow the baseline.

The branch-matched frontend client is generated from the backend in the same revision. The shared action is contract generation only; backend tests belong to the `Backend` job so they are not duplicated inside API generation.

## Code scanning

GitHub CodeQL **Default setup** is the repository's code-scanning owner.

Do not add `github/codeql-action` jobs to the normal CI while Default setup is enabled. GitHub rejects advanced-configuration uploads when Default setup owns the repository, creating duplicate work and permanently red checks rather than additional protection.

Whether code-scanning findings are merge-blocking is repository security/ruleset state and must be verified in GitHub settings. CI workflow YAML must not duplicate that external control.

## Main verification

The same `CI` workflow runs after a merge to `main`.

Core backend, frontend/API-contract and contract/documentation checks run again against the exact production revision. Code scanning remains owned by GitHub Default setup rather than being duplicated in the CI workflow.

The post-merge `CI Gate` is the backend deployment trigger. This gives Azure an exact successful `main` SHA to build and deploy.

Frontend production does not wait for the post-merge GitHub CI run: Vercel is configured for Git deployments from `main`. This is why the pull-request `CI Gate` must be required before merge.

## Production deployment

### Frontend

`src/FE/vercel.json` allows Git deployment from `main` and disables other branches. Its ignored-build command lets Vercel skip a deployment when the configured frontend project root did not change.

Therefore an explicit merge to `main` is the frontend production release action.

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

There is no release-branch handoff between CI and deployment.

### Infrastructure and critical-flow testing

Production infrastructure remains a separate manual operation through `.github/workflows/manual-production-infrastructure.yml`.

`.github/workflows/playwright-prod-smoke.yml` currently runs only the write-free public production smoke. Authenticated/destructive Playwright evidence is blocked until the isolated staging target and approved test authentication are completed; it is never a substitute for the pre-merge CI gate.

GitHub Pages remains an independent site/docs deployment concern.

## Repository protection

The `main` ruleset must enforce the delivery model, not merely document it:

- pull request required;
- `CI Gate` required;
- direct pushes blocked;
- force pushes blocked;
- merge remains an explicit human action.

PR #439 proved that the workflow alone is insufficient: GitHub allowed a merge while `CI Gate` was red. Ruleset verification is therefore a required operational step before WOR-382 is complete.

## Releases and tags

GitHub tags/releases are optional release-history markers for meaningful product versions. They do not control production deployment and should not recreate a second release pipeline.

## Security

Use GitHub OIDC for Azure deployment. Grant `id-token: write` only to the job that needs the Azure token. Do not introduce publish profiles, long-lived Azure credentials or privileged repository-writing automation without a concrete requirement and reviewed least-privilege design.

The durable decision behind this model is recorded in [`../architecture/adr/0005-main-as-production-boundary.md`](../architecture/adr/0005-main-as-production-boundary.md).
