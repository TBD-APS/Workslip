# Playwright critical-flow suite

**Status:** Active, manually triggered  
**Owner:** Frontend/API maintainers  
**Source of truth:** Runtime UI, runtime OpenAPI, endpoint code, `src/BE/WorkslipApi/Postman/postman_collection.json`, and GitHub Actions run evidence

## Purpose

`.github/workflows/playwright-prod-smoke.yml` runs mobile Chromium against `https://app.mrsoftware.dk`, which is currently the shared development environment. The workflow is manual and is not a required pull-request check.

The suite covers these selectable scenarios:

1. `auth-session`
2. `kls-lifecycle`
3. `rejection-loop`
4. `draft-recovery`
5. `role-tenant-isolation`
6. `invitation-onboarding`
7. `assignment-lifecycle`
8. `customer-lifecycle`
9. `worksheet-integrity`
10. `diverse-lifecycle`

`all-critical` executes all ten scenarios in isolated browser contexts and continues after individual failures so the artifact contains the complete result set. `public-smoke` remains a write-free availability check.

## Data and contract rules

The suite must not depend on pre-existing IDs, customers, jobs, users, reference-data values, addresses, or sort order.

- Runtime API contracts are loaded from the deployed `/openapi/v1.json`.
- Executable request examples and unique-value conventions are loaded from `Postman/postman_collection.json`.
- Installation types, users, customers, jobs, roles, and organization context are loaded from runtime API responses. Work kind and closure flag must exist in runtime reference data and match the executable `/api/jobs` example in the Postman collection.
- Postal addresses are selected from the DAWA autocomplete service and then entered through the real UI control.
- Missing assignable users and isolated tenant fixtures are created through documented API contracts using unique values derived from the Postman collection.
- User-visible state transitions are performed through the actual UI. Direct API calls are limited to fixture discovery/setup, assertions, tenant-boundary probes, and cleanup.

Every direct API call must exist in both runtime OpenAPI and the Postman collection. Missing contract coverage fails the scenario instead of falling back to guessed data or guessed request shapes.

## Test-data lifecycle

Jobs, customers, worksheets, and users created by a scenario are removed where a delete contract exists. Organization and invitation fixtures are retained and clearly prefixed because the current public contract has no corresponding delete operation. Retained and failed-cleanup fixtures are listed in `report.json`.

All generated fixtures include a `PLAYWRIGHT` marker and unique run identifier. The suite is approved only for an isolated development or integration environment.

## Authentication and sensitive evidence

The suite uses the deployed dev-login controls. Tokens are kept in memory and are never written to artifacts.

Authenticated Playwright traces are not uploaded because they can contain authorization headers, request bodies, and personal data. Artifacts contain redacted JSON reports and selected screenshots. Login steps do not take screenshots.

The invitation scenario verifies the real UI through the Microsoft handoff. Completing Microsoft enrollment requires an isolated third-party identity session and is reported as a coverage limitation when no such session is available; the suite must not commit credentials or authenticated storage state.

## Known product gap

The current rejection dialog and `ChangeJobStatusRequest` do not contain a rejection-reason field. The `rejection-loop` scenario therefore verifies the status transition, correction, resubmission, approval, and history, but it cannot verify a reason that the product does not currently store. The product correction is tracked in WOR-292.

## Running

Open **Actions → Playwright critical flows → Run workflow**, choose a scenario, and run it from the default branch. The resulting artifact is named `playwright-critical-flows-<scenario>` and is retained for seven days.

A passing workflow is evidence only for the selected scenario, deployed revision, environment, browser, and viewport recorded in the artifact.
