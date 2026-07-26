# CI quality gates

Status: Active  
Owner: Workslip repository owner  
Source of truth: `.github/workflows/`, repository rulesets and current successful workflow runs  
Review cadence: monthly and whenever a workflow or required check changes  
Linear: WOR-170, WOR-171

## Principle

A required or routinely triggered check must be configured, actionable and owned. Placeholder workflows that fail on every pull request reduce trust in CI and must not remain active.

## Current expectations

- Documentation Quality validates maintained Markdown, local links and API endpoint-catalog drift.
- Jekyll site validation builds the public site with frozen dependencies and validates generated output.
- Pages deployment builds and validates the site before uploading the Pages artifact.
- Existing functioning code, security and review checks remain governed by repository rulesets and their own configuration.

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

A workflow file change alone does not prove that GitHub rulesets were updated.
