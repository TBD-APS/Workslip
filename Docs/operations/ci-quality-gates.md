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

It generates the frontend client from the pull request's own backend OpenAPI contract, then runs frontend lint, Vitest and the production build. Contract generation is isolated from production SQL/runtime startup. See [`BRANCH_MATCHED_FRONTEND_VALIDATION.md`](BRANCH_MATCHED_FRONTEND_VALIDATION.md) for the maintained boundary and troubleshooting details.

This is a targeted frontend/API-contract pull-request gate, not a general full-repository validation workflow. A workflow file proves intended execution only; whether GitHub currently requires its status is repository-ruleset state and must be verified in GitHub settings.

### Release validation

`.github/workflows/release-validation.yml` runs for pushes to `release/**` and is the full-code release validation boundary.

It currently covers:

- backend Release build, backend tests and C# CodeQL;
- frontend lint, Vitest, production build and JavaScript/TypeScript CodeQL;
- release-environment policy plus Playwright/Postman source checks;
- a final `Release gate` that succeeds only when the required jobs succeed.

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

`.github/workflows/backend-production-release.yml` is manual-only. A merge to `main` does not deploy the backend.

To deploy a backend release candidate:

1. merge the candidate commit to `main`, then create or update a `release/**` branch at that exact commit;
2. wait for that branch's `Release validation` push workflow to complete successfully;
3. open **Actions → Backend API release deploy → Run workflow** and keep **Use workflow from** set to `main`;
4. supply the release branch name as `release_ref` and its full 40-character lowercase head SHA as `candidate_sha`;
5. review the final workflow summary after health verification.

The deployment orchestrator is trusted only when it runs from the current `main` version of `.github/workflows/backend-production-release.yml`. The candidate gate fails before source checkout, build, `prod` environment access or Azure authentication unless the release input is a `release/**` branch whose current head is the exact candidate SHA, that SHA is contained in current `main`, its `release-validation.yml` Git blob matches current `main`, and a completed successful push run of that workflow exists for the same branch and SHA. A branch moved before gate evaluation, partial or different SHA, release-only commit, altered or older validation workflow, untrusted orchestrator ref, or missing successful validation all reject the deployment.

After the gate, the build checks out the validated SHA directly and records a SHA-256 digest for the deployment package. The deploy job verifies that digest before Azure authentication. Only the deploy job receives GitHub OIDC permission; the Azure target, diagnostics prerequisite, release-testing policy, bounded deployment retries and API health verification remain unchanged. A final evidence summary is written only after health succeeds and relates the `main` orchestration SHA, release ref, candidate SHA, validation workflow blob/run, artifact digest, health URL/result and deployment run.

The `prod` GitHub Environment is the external backstop and must use a custom deployment branch policy that allows only `main`; secrets and reviewer behavior are independent settings. Verify that policy in GitHub whenever this workflow or environment configuration changes. Workflow YAML alone cannot enforce or prove the environment policy. The legacy `.github/workflows/main_api-mrsoftware-prod.yml` workflow ID is disabled and must remain disabled; the gated workflow uses a new path and workflow ID so pre-gate runs are not a normal rerun path. GitHub administrators retain their existing break-glass ability to change or bypass repository controls, which is outside the normal release path.

This backend gate is the scope of WOR-368. Frontend/Vercel production policy is separate and remains tracked by WOR-369; the backend workflow does not establish or change that boundary.

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
