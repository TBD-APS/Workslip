# CI quality gates

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** `.github/workflows/`, repository rulesets and current successful runs  
**Review cadence:** When workflows, release environments or required checks change

## Principle

A workflow should exist only when it provides an actionable signal or performs a required deployment task. Do not keep duplicated, placeholder or routinely ignored automation.

Workflow files describe intended automation; successful runs and target-environment checks provide execution evidence.

## Current boundaries

### Frontend pull-request validation

`.github/workflows/frontend-validation.yml` runs on pull requests to `main` when the frontend, backend API, shared API-generation action or that workflow changes.

The frontend currently carries inherited ESLint debt. Pull-request validation therefore uses a **no-new-errors ratchet** instead of treating the inherited debt as a permanently failing gate:

- run ESLint against the pull request checkout and capture JSON findings;
- run ESLint against the pull request base branch in a separate clean worktree with that branch's dependency graph;
- compare only severity-2 ESLint errors using stable source fingerprints rather than line numbers;
- fail when the pull request introduces an error fingerprint or additional occurrence that is not present on the base branch;
- allow inherited errors to remain temporarily and report warnings without making them blocking.

The ratchet itself has focused Node tests. Existing errors should still be removed under normal maintenance; the ratchet is not permission to disable correctness rules or grow the baseline.

After the lint ratchet, the workflow generates the frontend client from the pull request's own backend OpenAPI contract, then runs Vitest and the production build. Contract generation is isolated from production SQL/runtime startup. See [`BRANCH_MATCHED_FRONTEND_VALIDATION.md`](BRANCH_MATCHED_FRONTEND_VALIDATION.md) for the maintained boundary and troubleshooting details.

This is a targeted frontend/API-contract pull-request gate, not a general full-repository validation workflow. A workflow file proves intended execution only; whether GitHub currently requires its status is repository-ruleset state and must be verified in GitHub settings.

### Release validation

`.github/workflows/release-validation.yml` runs for pushes to `release/**` and is the full-code release validation boundary.

It currently covers:

- backend Release build, backend tests and C# CodeQL;
- frontend inherited-lint inventory, Vitest, production build and JavaScript/TypeScript CodeQL;
- release-environment policy plus Playwright/Postman source checks;
- a final `Release gate` that succeeds only when the required jobs succeed.

Known inherited ESLint debt is reported during release validation but is not allowed to make every release permanently red. New ESLint errors are blocked earlier by the pull-request ratchet before code reaches `main`. An ESLint execution/configuration failure still fails release validation.

Other issue-scoped/local validation remains required when the changed risk is not covered by the targeted pull-request workflow.

### Release candidate process

Normal implementation stays on one Linear issue, one `rbj--<issue>-...` branch and one focused pull request. `main` is the integration branch.

A production candidate is an ephemeral `release/**` branch created from an exact commit already contained in `main`, for example `release/2026-08-08-rc1`. The release branch is a candidate pointer, not a development branch:

1. select the exact `main` commit intended for release;
2. create the `release/**` branch at that SHA;
3. wait for the `Release validation` workflow to succeed for that branch and SHA;
4. deploy the exact validated SHA;
5. run the required target-environment smoke checks;
6. tag/version the deployed commit when the release is accepted.

Do not fix defects directly on a release branch. Fix the owning/new Linear issue on a normal `rbj--...` branch, merge it through a focused PR to `main`, then create a new candidate such as `rc2`.

Moving a release branch after validation invalidates it for backend deployment because the production workflow requires the branch's current head, the supplied SHA and the successful validation run to identify the same commit.

### Backend production deployment

`.github/workflows/main_api-mrsoftware-prod.yml` is manual-only. It must be dispatched from `main` with:

- `release_ref`: an existing `release/**` branch;
- `release_sha`: the full 40-character SHA that branch currently points to.

Before building or contacting Azure, the workflow verifies that:

- the dispatch itself uses the maintained workflow from `main`;
- the supplied ref is a `release/**` branch;
- the release branch currently points to the supplied SHA;
- the candidate SHA is contained in current `main`;
- `Release validation` has a completed successful push run for that same branch and SHA.

The API artifact is then built from that exact SHA. Only the deploy job receives `id-token: write`, binds to the `prod` GitHub Environment, uses Azure OIDC, applies the release-testing policy, deploys with bounded retries and verifies `/health`.

The workflow logs the release ref and SHA so deployment evidence identifies the code that was actually released. Deployment success is not a substitute for authentication, database or critical-flow smoke when those paths changed.

### Frontend production deployment

Vercel production deployment policy is defined from the frontend project/configuration. Repository workflow documentation should not duplicate Vercel dashboard state that cannot be proven from the repository.

Until that external configuration is explicitly verified, backend release-candidate gating must not be presented as proof that frontend and backend production deployments share the same release boundary.

### Documentation checks

`python tools/docs/check_docs.py` is the local documentation drift check. It is deliberately not a broad automatic pull-request workflow; reviewers run it when documentation or documentation-owning sources change.

## Required-check changes

When adding, renaming or removing a required check:

1. update the workflow and repository ruleset together;
2. prove the new check can succeed on its intended branch/event;
3. prove a controlled failure blocks or reports as intended;
4. remove stale required-check names;
5. document the owner and remediation path when the check is non-obvious.

A YAML change alone does not prove repository ruleset configuration.

## Security

Use GitHub OIDC for Azure deployment. Grant `id-token: write` only to the job that needs the Azure token. Do not introduce publish profiles, long-lived Azure credentials or privileged repository-writing automation without a concrete requirement and reviewed least-privilege design.
