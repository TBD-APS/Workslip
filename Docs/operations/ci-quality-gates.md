# CI quality gates

Status: Active  
Owner: Workslip repository owner  
Source of truth: `.github/workflows/`, `src/FE/config/release-environments.json`, repository rulesets and current successful workflow runs  
Review cadence: monthly and whenever a workflow, release environment or required check changes  
Linear: WOR-170, WOR-171, WOR-188, WOR-194, WOR-303, WOR-305, WOR-306, WOR-308, WOR-313

## Principle

A required or routinely triggered check must be configured, actionable and owned. Placeholder, overlapping or routinely ignored workflows reduce trust in CI and must not remain active.

## Current expectations

- Pages deployment builds and validates the Jekyll site before deploying relevant changes from `main`; there is no pull-request Jekyll workflow.
- API deployment restores, builds, publishes and deploys the backend artifact for relevant changes on `main` or an explicit manual run.
- API deployment resolves `src/FE/config/release-environments.json` and applies the production `ReleaseTesting__Enabled` value to Azure App Service before deployment.
- API deployment does not invoke the post-deploy cache workflow.
- Vercel Git deployments are enabled only for `main`; all other branch names are denied by the repository's `src/FE/vercel.json` policy.
- Every push to `release/**` runs `.github/workflows/release-validation.yml` without path filters.
- C# and JavaScript/TypeScript CodeQL analysis runs only inside that release workflow.
- The release workflow validates the central environment policy and release-test source guards.
- A successful production backend release or successful Vercel production deployment from `main` or `release/**` triggers `.github/workflows/update-repomix-after-release.yml`.
- The Repomix workflow resolves the released branch by SHA, regenerates `repomix-output.xml`, and commits only when the generated output changed.
- Failed, cancelled, preview, stale, or unrelated deployments do not update Repomix.
- The repository has no general pull-request validation workflow. Relevant backend/frontend validation must be run locally or through a deliberately added, issue-scoped validation workflow that is removed again after use.
- Other security and review checks remain governed by repository rulesets and their own configuration.

## Release branch validation

`release/**` is the automatic full-code validation boundary before production promotion. The workflow runs all mandatory jobs for every push, even when only one part of the repository changed. This intentionally trades some CI time for a stable, predictable release signal.

The workflow exposes the following separate checks:

- `Backend build, tests and CodeQL` — initializes CodeQL for C#, restores the full backend solution, performs an instrumented Release build, runs the complete backend test suite and publishes the C# analysis;
- `Frontend lint, tests, build and CodeQL` — initializes CodeQL for JavaScript/TypeScript, installs from the committed lockfile, runs ESLint, runs Vitest once, builds the production frontend and publishes the frontend analysis;
- `Playwright and API contract sources` — validates `src/FE/config/release-environments.json`, runs its policy regression tests, syntax-checks the maintained Playwright scenario modules and parses the Postman collection;
- `Release gate` — succeeds only when all required workflow jobs, including both CodeQL analyses, succeeded.

Superseded pushes to the same release branch cancel the older run. NuGet, npm and generated-font caches are used to reduce repeat runtime. Backend TRX output is retained for three days; application builds and local browser artifacts are not committed.

The workflow has no `workflow_dispatch` or scheduled trigger. It runs only for pushes whose ref matches `release/**`. A failed or cancelled run can be rerun from the GitHub Actions run page without adding another trigger.

### CodeQL setup

Workslip uses CodeQL advanced setup inside `.github/workflows/release-validation.yml` because default setup cannot be restricted to release branches only. Default setup automatically scans the default branch, protected branches, relevant pull requests and a weekly schedule.

The release workflow uses:

- `github/codeql-action/init@v4` with `build-mode: manual` for C# before the existing Release build;
- `github/codeql-action/init@v4` with `build-mode: none` for JavaScript/TypeScript;
- `github/codeql-action/analyze@v4` inside the corresponding backend and frontend jobs;
- workflow permissions limited to read access plus `security-events: write` for SARIF publication.

Do not add a separate CodeQL workflow. Keeping analysis inside the backend and frontend release jobs avoids duplicate builds, duplicate workflow names and competing CodeQL configurations.

Repository administration must complete and verify WOR-310:

1. disable CodeQL default setup under `Settings → Advanced Security`;
2. remove the CodeQL code-scanning rule from the ruleset that targets only `main`;
3. create or update the release ruleset so it targets `refs/heads/release/**` and requires CodeQL code-scanning results at the approved severity threshold;
4. require the stable status check `Release gate` on the same release ruleset;
5. prove both languages upload successfully on a real release branch.

Leaving default setup enabled creates overlapping analysis and can block uploads from advanced setup. Leaving the `main` CodeQL rule active after moving analysis to release branches can block ordinary main PRs because the required analysis no longer exists there.

### Required release ruleset

Create or maintain a repository ruleset targeting `refs/heads/release/**` with:

1. direct pushes restricted to the intended release maintainers;
2. required status check `Release gate`;
3. required CodeQL code-scanning results at the approved severity threshold;
4. force pushes blocked;
5. no broad GitHub App bypass beyond identities with a documented release need.

The workflow file proves intended automation only. A real release push must prove that every job executes, both CodeQL analyses publish, `Release gate` reports the expected result, and a controlled failing push is blocked by the ruleset.

### Release environment and browser boundary

`src/FE/config/release-environments.json` defines the current operating phase and which environment may expose release-test endpoints or run write-capable Playwright scenarios. It lives inside the Vercel project root so the frontend build always receives the reviewed policy without relying on optional access to parent directories. Backend and CI workflows consume the same file.

Before first customer go-live, the only deployed production slot contains no active customers and is intentionally the pre-live release-test environment. Full Playwright may therefore run directly against `https://app.mrsoftware.dk`, with synthetic data and cleanup, while the policy explicitly marks production as pre-live and write-enabled.

This exception ends before customer access opens. The live policy is valid only when:

- production development endpoints and destructive Playwright are disabled;
- a dedicated staging HTTPS origin exists;
- staging carries the full release-test suite;
- production receives only write-free post-deploy smoke.

The resolver rejects an unsafe live configuration without staging. The Playwright release runner rejects write-capable scenarios when the selected target does not permit them. The API deployment propagates the production endpoint setting into Azure so a source-only policy change is not left unapplied at runtime.

The Vite build also consumes the policy. Dev-login controls require both the committed target policy and `VITE_ENABLE_DEV_LOGIN=true`. An invalid or absent `VITE_RELEASE_TARGET` falls back to production. The future staging frontend must explicitly set `VITE_RELEASE_TARGET=staging`; production should set `VITE_ENABLE_DEV_LOGIN=false` at go-live as defense in depth.

Backend endpoint removal remains the security boundary; hiding frontend controls is UX hardening and protection against deployment-variable drift, not authorization.

WOR-309 owns creation of the second environment. WOR-313 owns the fail-closed transition configuration.

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

A failure in this maintenance workflow does not roll back an already successful application release. Treat the failed workflow as repository-maintenance debt and rerun it after correcting app installation, credentials, package resolution or branch-protection configuration.

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
2. Prove the new check succeeds on an ordinary release push.
3. Prove a controlled failure blocks or reports as intended.
4. Document ownership and remediation steps.
5. Remove obsolete workflow files and stale required-check references together.

A workflow file change alone does not prove that GitHub rulesets were updated. After merging WOR-194, remove a stale required check named `Validate documentation` or `Documentation Quality` if one exists. Also remove any remaining stale requirements from WOR-188: `Full Stack Validation`, `React Doctor`, `Application Validation` and `Validate Jekyll site`.
