# Playwright critical-flow suite

**Status:** Active, manually triggered  
**Owner:** Frontend/API maintainers  
**Source of truth:** Runtime UI, runtime OpenAPI, endpoint code, `src/BE/WorkslipApi/Postman/postman_collection.json`, local run evidence, and GitHub Actions run evidence

## Purpose

`.github/workflows/playwright-prod-smoke.yml` runs mobile Chromium against `https://app.mrsoftware.dk`, which is currently the shared development environment. The workflow is manual and is not a pull-request check or deployment step. It therefore adds no time to normal frontend or backend deployment.

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

`all-critical` expands into ten independent GitHub Actions matrix jobs with `max-parallel: 4`. Each flow has its own browser process, report, screenshots, cleanup, result, and artifact. This reduces wall-clock time without hiding individual failures. `public-smoke` remains a write-free availability check.

## Runtime optimization

The workflow uses Microsoft's version-matched `mcr.microsoft.com/playwright:v1.55.0-noble` image. Chromium and its Linux system dependencies are already in that image, so the job does not run `playwright install --with-deps` on every invocation.

Only the isolated `playwright@1.55.0` Node runtime is installed under `src/FE/scripts/node_modules`. The full frontend dependency graph is not installed for a deployed smoke run. The per-flow timeout is 35 minutes rather than the previous 90-minute suite timeout.

The first local Docker run must download the Playwright image. Later runs reuse Docker's local image cache. GitHub-hosted timing must be measured from a real workflow run before making a specific duration claim.

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

The suite uses the deployed dev-login controls. The authenticated scenarios fail when those controls or the dev-token endpoint are unavailable; they must not silently switch to embedded credentials or assumed users. Tokens are kept in memory and are never written to artifacts.

Authenticated Playwright traces are not uploaded because they can contain authorization headers, request bodies, and personal data. Artifacts contain redacted JSON reports and selected screenshots. Login steps do not take screenshots.

The invitation scenario verifies the real UI through the Microsoft handoff. Completing Microsoft enrollment requires an isolated third-party identity session and is reported as a coverage limitation when no such session is available; the suite must not commit credentials or authenticated storage state.

## Known product gap

The current rejection dialog and `ChangeJobStatusRequest` do not contain a rejection-reason field. The `rejection-loop` scenario therefore verifies the status transition, correction, resubmission, approval, and history, but it cannot verify a reason that the product does not currently store. The product correction is tracked in WOR-292.

## Local Windows validation

Use `tools/playwright/run-critical-local.ps1` from the repository root. It supports two modes.

### Fast direct run

This runs the exact Node scenario implementation without Docker or GitHub Actions emulation. It validates source syntax, parses the Postman collection, installs only the isolated Playwright runtime and Chromium when missing, runs the selected scenario, and opens the local evidence folder.

Prerequisite: Node.js 20 or newer; Node.js 22 is recommended.

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Direct `
  -Scenario public-smoke
```

After the public smoke passes, run one authenticated flow:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Direct `
  -Scenario kls-lifecycle
```

The direct `all-critical` mode remains sequential inside one local process. Use it only after individual flows work.

### Actual YAML through `act`

This executes `.github/workflows/playwright-prod-smoke.yml` locally through Docker. It validates the workflow wiring, matrix expression, container image, runtime installation, source validation, environment variables, and scenario command.

Prerequisites:

- Docker Desktop is installed and running.
- `act` is installed.

```powershell
winget install Docker.DockerDesktop
winget install nektos.act
```

Run the write-free workflow first:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Workflow `
  -Scenario public-smoke
```

Then run one authenticated scenario:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Workflow `
  -Scenario kls-lifecycle
```

The helper creates the `workflow_dispatch` event JSON in the temporary directory, pre-pulls the version-matched Playwright image when necessary, calls `act`, removes the event file, and opens `artifacts/playwright-prod-smoke`.

`act` sets `ACT=true`. The workflow therefore skips GitHub's artifact-upload action locally while preserving screenshots and `report.json` in the mounted repository workspace. Local evidence is ignored by Git.

A successful `act` run proves that the workflow can execute in the local Docker emulation. It does not prove that GitHub-hosted runner permissions, network access, or the target environment are identical. One real GitHub Actions run is still required before merge-readiness can be claimed.

## GitHub Actions run

Open **Actions → Playwright critical flows → Run workflow**, choose a scenario, and run it from the default branch. A single scenario produces `playwright-critical-flows-<scenario>`. `all-critical` produces one artifact per scenario, retained for seven days.

A passing workflow is evidence only for the selected scenario, deployed revision, environment, browser, and viewport recorded in the artifact.
