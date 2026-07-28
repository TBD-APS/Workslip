# CI quality gates

Status: Active  
Owner: Workslip repository owner  
Source of truth: `.github/workflows/`, repository rulesets and current successful workflow runs  
Review cadence: monthly and whenever a workflow or required check changes  
Linear: WOR-170, WOR-171, WOR-188, WOR-194

## Principle

A required or routinely triggered check must be configured, actionable and owned. Placeholder, overlapping or routinely ignored workflows reduce trust in CI and must not remain active.

## Current expectations

- Pages deployment builds and validates the Jekyll site before deploying relevant changes from `main`; there is no pull-request Jekyll workflow.
- API deployment restores, builds, publishes and deploys the backend artifact for relevant changes on `main` or an explicit manual run.
- API deployment does not invoke the post-deploy cache workflow.
- Vercel Git deployments are enabled only for `main`; all other branch names are denied by the repository's `src/FE/vercel.json` policy.
- The repository has no general pull-request validation workflow. Relevant backend/frontend validation must be run locally or through a deliberately added, issue-scoped validation workflow that is removed again after use.
- Existing security and review checks supplied outside these workflow files remain governed by repository rulesets and their own configuration.

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
