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

`.github/workflows/frontend-validation.yml` is the unified `CI` workflow. It runs for pull requests to `main` and for stacked pull requests whose base branch matches `rbj--**`. That lets each stacked PR keep a focused diff against its parent while still receiving the same build/test gate.

The required merge signal is the `CI Gate` job. It succeeds only when these jobs succeed:

- `Backend` — full Release restore, build and backend test suite.
- `Frontend + API contract` — no-new-errors ESLint ratchet, branch-matched OpenAPI/Orval generation, Vitest and production frontend build.
- `Contracts + docs` — production release-policy checks, Playwright source checks, synthetic-auth tests, Postman JSON validation and `python tools/docs/check_docs.py`.

CodeQL is not part of the active CI gate. WOR-382 removed the unstable advanced CodeQL jobs rather than carrying a routinely failing security signal. Static/security analysis should only be reintroduced when it is stable, actionable and deliberately included in the required-check model.

The frontend carries inherited ESLint debt. CI therefore compares the pull-request findings with the exact base revision and blocks new severity-2 errors without treating inherited findings as permission to grow the baseline.

The branch-matched frontend client is generated from the backend in the same revision. The shared action is contract generation only; backend tests belong to the `Backend` job so they are not duplicated inside API generation.

## Main verification

The same `CI` workflow runs after a merge to `main`.

Core backend, frontend/API-contract and contract/documentation checks run again against the exact production revision.

The post-merge `CI Gate` is the backend deployment trigger. This gives Azure an exact successful `main` SHA to build and deploy.

Frontend production does not wait for the post-merge GitHub CI run: Vercel is configured for Git deployments from `main`. This is why the pull-request `CI Gate`, especially the production frontend build, must be required before merge.

## Stacked pull requests

A stack keeps each Linear issue in its own branch/PR. Child PRs target the immediately preceding `rbj--...` branch, not `main`, until their parent is merged.

Example:

```text
main
  └── rbj--wor-382-...
       └── rbj--wor-367-...
            └── rbj--wor-160-...
                 └── rbj--wor-364-...
```

Each child still runs `CI Gate`. Merge from the bottom of the dependency graph: parent first. After a parent is squash-merged, restack/rebase the next child onto the new `main` before merging it so the child PR does not carry obsolete parent commits.

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

Deployed Playwright scenarios remain available through `.github/workflows/playwright-prod-smoke.yml`. They are used when the changed risk requires target-environment evidence; they are not a substitute for the pre-merge CI gate.

GitHub Pages remains an independent site/docs deployment concern.

## Repository protection

The `main` ruleset should enforce the delivery model, not merely document it:

- pull request required;
- `CI Gate` required;
- direct pushes blocked;
- force pushes blocked;
- merge remains an explicit human action.

Workflow YAML does not prove repository ruleset state. Required-check configuration must be verified in GitHub whenever the gate name or ruleset changes.

## Releases and tags

GitHub tags/releases are optional release-history markers for meaningful product versions. They do not control production deployment and should not recreate a second release pipeline.

## Security

Use GitHub OIDC for Azure deployment. Grant `id-token: write` only to the job that needs the Azure token. Do not introduce publish profiles, long-lived Azure credentials or privileged repository-writing automation without a concrete requirement and reviewed least-privilege design.

The durable decision behind this model is recorded in [`../architecture/adr/0005-main-as-production-boundary.md`](../architecture/adr/0005-main-as-production-boundary.md).