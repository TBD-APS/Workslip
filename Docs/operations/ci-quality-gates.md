# CI quality gates

Status: Active  
Owner: Workslip repository owner  
Source of truth: `.github/workflows/`, repository rulesets and current successful workflow runs  
Review cadence: monthly and whenever a workflow or required check changes  
Linear: WOR-170, WOR-171, WOR-188, WOR-194, WOR-303, WOR-305, WOR-306, WOR-308

## Principle

A required or routinely triggered check must be configured, actionable and owned. Placeholder, overlapping or routinely ignored workflows reduce trust in CI and must not remain active.

## Current expectations

- Pages deployment builds and validates the Jekyll site before deploying relevant changes from `main`; there is no pull-request Jekyll workflow.
- API deployment restores, builds, publishes and deploys the backend artifact for relevant changes on `main` or an explicit manual run.
- API deployment does not invoke the post-deploy cache workflow.
- Vercel Git deployments are enabled only for `main`; all other branch names are denied by the repository's `src/FE/vercel.json` policy.
- Every push to `release/**` runs `.github/workflows/release-validation.yml` without path filters.
- A successful production backend release or successful Vercel production deployment from `main` or `release/**` triggers `.github/workflows/update-repomix-after-release.yml`.
- The Repomix workflow resolves the released branch by SHA, regenerates `repomix-output.xml`, and commits only when the generated output changed.
- Failed, cancelled, preview, stale, or unrelated deployments do not update Repomix.
- The repository has no general pull-request validation workflow. Relevant backend/frontend validation must be run locally or through a deliberately added, issue-scoped validation workflow that is removed again after use.
- Existing security and review checks supplied outside these workflow files remain governed by repository rulesets and their own configuration.

## Release branch validation

`release/**` is the automatic full-code validation boundary before production promotion. The workflow runs all mandatory jobs for every push, even when only one part of the repository changed. This intentionally trades some CI time for a stable, predictable release signal.

The workflow exposes the following separate checks:

- `Backend build and tests` — restores the full backend solution, builds it in Release mode and runs the complete backend test suite;
- `Frontend lint, tests and build` — installs from the committed lockfile, runs ESLint, runs Vitest once and builds the production frontend;
- `Playwright and API contract sources` — syntax-checks the maintained Playwright scenario modules and parses the Postman collection;
- `Release gate` — succeeds only when all required workflow jobs succeeded.

Superseded pushes to the same release branch cancel the older run. NuGet and npm dependency caches are used to reduce repeat runtime. Backend TRX output is retained for three days; application builds and local browser artifacts are not committed.

### Required release ruleset

Create or maintain a repository ruleset targeting `refs/heads/release/**` with:

1. direct pushes restricted to the intended release maintainers;
2. required status check `Release gate`;
3. required CodeQL code-scanning results at the approved severity threshold;
4. force pushes blocked;
5. no broad GitHub App bypass beyond identities with a documented release need.

CodeQL default setup analyzes the default branch and protected branches. Protecting `release/**` through the release ruleset is therefore part of the release-gate configuration. Do not add a second advanced CodeQL workflow while default setup is active, because competing CodeQL setup types create duplicate or stale analysis configurations.

The workflow file proves intended automation only. The first push to a real `release/**` branch must prove that every job executes, that `Release gate` reports the expected result, and that a controlled failing push is blocked by the ruleset.

### Browser validation boundary

The release workflow validates Playwright source and API contract material, but it does not call production and describe that as release-commit browser validation. `https://app.mrsoftware.dk` contains the currently deployed production revision, not necessarily the release branch SHA.

Automatic browser validation of the exact release commit requires an isolated release/staging frontend and API with synthetic data and safe test identities. Until that environment exists and is tied to the release SHA, run the relevant Playwright scenario deliberately and report the release as browser-unvalidated. Do not use destructive production flows as a substitute.

## Post-release Repomix update

The Repomix snapshot is maintained as a post-release artifact rather than a release prerequisite:

- Backend releases qualify only when `Backend API deploy` completes successfully. That workflow reports success only after the Azure deployment and API health check pass.
- Frontend releases qualify only for a successful GitHub `deployment_status` with environment `Production` whose deployed SHA matches the current tip of `main` or `release/**`.
- The workflow regenerates from the latest remote target branch and retries if that branch advances while publishing.
- Repomix is pinned to version `1.13.0` to keep generated output deterministic across runs.
- The generated commit uses `[skip ci]` and does not modify application paths, preventing the backend workflow from retriggering.
- Before commit, the workflow verifies that no tracked or untracked path other than `repomix-output.xml` changed.
- Before push, the workflow verifies that the staged change set contains exactly `repomix-output.xml`.
- If `repomix-output.xml` is unchanged, the workflow exits without creating a commit.

### Protected-branch write identity

GitHub branch protection and repository rulesets grant bypass to actors, not individual files. The Repomix workflow therefore uses a dedicated GitHub App plus workflow-level file enforcement:

1. Install the dedicated Repomix GitHub App only on `Workslip-v2.0`.
2. Grant the app repository `Contents: Read and write` access and no broader permission than required.
3. Store the app ID as the Actions repository variable `REPOMIX_APP_ID`.
4. Store the app private key as the Actions repository secret `REPOMIX_APP_PRIVATE_KEY`.
5. Add only that GitHub App to the bypass list for the pull-request requirement covering `main` and `release/**`, using the narrowest available bypass mode.
6. Do not reuse the private key in other workflows.
7. Keep `.github/workflows/update-repomix-after-release.yml` protected through normal pull-request review, because this file controls how the privileged app identity is used.

The workflow uses `actions/create-github-app-token` to create a short-lived installation token for the current run. The default `GITHUB_TOKEN` remains read-only. Missing or invalid app credentials fail during token creation; an app without ruleset bypass fails at push with an explicit remediation message.

A failure in this maintenance workflow does not roll back an already successful application release. Treat the failed workflow as repository-maintenance debt and rerun it after correcting app installation, credentials, package resolution, or branch-protection configuration.

## Removed workflow decisions

The following workflows were removed under WOR-188:

- `Full Stack Validation`: expensive SQL/API/frontend/Postman/Selenium execution on routine application pull requests.
- `React Doctor`: broad third-party analysis triggered for unrelated repository changes.
- `Application Validation`: overlapping partial checks that did not provide a reliable general application gate.
- `Validate Jekyll site`: duplicated the build and generated-output validation performed by the Pages deployment workflow.
- `Linear Release`: automatic release synchronization on every push to `main` was not required.

`Documentation Quality` was removed under WOR-194 because its broad path filters made it run on routine backend and frontend pull requests. The documentation validator scripts remain available for deliberate local use; they are not an automatic repository gate.

The post-deploy cache workflow remains available for explicit manual execution, but it is not part of the production API deployment chain.

## SonarCloud decision

The generated SonarCloud template was removed because it had no project key, no organization key and no repository checkout step. It therefore produced a permanent failing check without reliable analysis.

SonarCloud may be reintroduced only when all of the following are ready:

1. A named owner is assigned.
2. The repository is imported into the intended SonarCloud organization.
3. Project and organization identifiers are stored in reviewed configuration.
4. `SONAR_TOKEN` is configured with minimum required access.
5. The workflow checks out the exact source revision and scans the intended frontend/backend scope.
6. Duplicate automatic analysis is disabled or intentionally coordinated.
7. A successful non-production pull request run is recorded.
8. The required-check decision and failure-response procedure are documented.

Do not restore a copied starter workflow with empty values.

## Required-check changes

When adding, renaming or removing a workflow check:

1. Inspect repository rulesets and branch protection for stale required-check names.
2. Prove the new check succeeds on an ordinary pull request.
3. Prove a controlled failure blocks or reports as intended.
4. Document ownership and remediation steps.
5. Remove obsolete workflow files and stale required-check references together.

A workflow file change alone does not prove that GitHub rulesets were updated. After merging WOR-194, remove a stale required check named `Validate documentation` or `Documentation Quality` if one exists. Also remove any remaining stale requirements from WOR-188: `Full Stack Validation`, `React Doctor`, `Application Validation` and `Validate Jekyll site`.
