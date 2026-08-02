# Playwright critical-flow suite

**Status:** Active, manually triggered  
**Owner:** Frontend/API maintainers  
**Source of truth:** `src/FE/config/release-environments.json`, runtime UI, runtime OpenAPI, endpoint code, `src/BE/WorkslipApi/Postman/postman_collection.json`, local run evidence, and GitHub Actions run evidence

## Purpose

`.github/workflows/playwright-prod-smoke.yml` runs mobile Chromium against an explicitly selected release-test target.

Before first customer go-live, `https://app.mrsoftware.dk` is the only deployed environment and is intentionally configured as **pre-live production**. It contains no active customers and is therefore used as the release-test environment, including authenticated scenarios that write synthetic data and clean it up afterward.

After customer go-live, Workslip moves to two environments:

- production allows only the write-free `public-smoke` scenario;
- staging hosts dev-login/release-test endpoints and the full critical-flow suite.

The workflow is manual and is not a pull-request check or deployment step. It therefore adds no time to normal frontend or backend deployment.

## Central release-environment policy

`src/FE/config/release-environments.json` is the reviewed source of truth. It intentionally lives inside the Vercel frontend root so the production build cannot depend on the optional Vercel setting that includes files outside a project's root directory. Backend and repository workflows read the same file.

`tools/release/resolve-release-environment.mjs` validates the policy before API deployment, release validation, local runs, and GitHub-hosted Playwright runs.

The current pre-live state is:

| Environment | URL | Dev/release endpoints | Destructive Playwright |
|---|---|---:|---:|
| Production | `https://app.mrsoftware.dk` | Enabled | Enabled |
| Staging | Not configured | Disabled | Disabled |

The required live state is:

| Environment | URL | Dev/release endpoints | Destructive Playwright |
|---|---|---:|---:|
| Production | `https://app.mrsoftware.dk` | Disabled | Disabled |
| Staging | Dedicated staging origin | Enabled | Enabled |

The resolver rejects unsafe intermediate combinations. In particular:

- live production cannot expose development endpoints or permit destructive Playwright;
- the live phase cannot be selected before a runnable staging origin exists;
- destructive Playwright cannot be enabled where development endpoints are disabled;
- target URLs must be clean HTTPS origins without credentials, path, query, or fragment.

The backend API deployment reads the production entry and applies `ReleaseTesting__Enabled` to Azure App Service before deploying. Missing or invalid backend configuration is fail-closed: outside ASP.NET Development, release-test endpoints are absent unless the resolved value is exactly `true`.

The frontend Vite build reads the same policy. Dev-login controls are rendered only when both conditions hold:

- `VITE_ENABLE_DEV_LOGIN=true` for the deployment;
- the selected release target enables development endpoints in the committed policy.

An invalid or missing `VITE_RELEASE_TARGET` defaults to `production`, which is the safe behavior after go-live. The future staging project must explicitly set `VITE_RELEASE_TARGET=staging`.

`DeveloperExceptionPage` is restricted to ASP.NET Development and is never enabled by the pre-live release-test setting.

## Go-live switch

Before inviting the first customer:

1. deploy a separate staging frontend, API, database, test identities, and synthetic fixtures;
2. change `src/FE/config/release-environments.json` from `prelive` to `live`;
3. set production `enableDevelopmentEndpoints` and `allowDestructivePlaywright` to `false`;
4. configure the staging HTTPS origin and set both staging flags to `true`;
5. set the staging Vercel variable `VITE_RELEASE_TARGET=staging` and keep `VITE_ENABLE_DEV_LOGIN=true` there;
6. set the production Vercel variable `VITE_ENABLE_DEV_LOGIN=false` as defense in depth;
7. deploy the API so `ReleaseTesting__Enabled=false` reaches production;
8. verify `/api/dev/token`, runtime OpenAPI, and Scalar are unavailable in production;
9. run `public-smoke` against production and the critical suite against staging.

WOR-309 tracks creation of the second environment. The configuration switch itself must be reviewed and merged before customer access opens.

## Scenarios

The suite covers these selectable scenarios:

1. `auth-session`
2. `kls-lifecycle`
3. `rejection-loop`
4. `draft-recovery`
5. `notification-navigation`
6. `role-tenant-isolation`
7. `invitation-onboarding`
8. `assignment-lifecycle`
9. `customer-lifecycle`
10. `worksheet-integrity`
11. `diverse-lifecycle`

`notification-navigation` logs in through the deployed UI, waits for the deployed service worker to control the mobile Chromium page, dispatches a standards-based synthetic `PushEvent` inside that worker, and verifies that the resulting notification click routes the existing app client to the requested authenticated route without opening an extra page. It also fails if API/authenticated routes enter a service-worker cache or if the runtime asset cache contains anything outside same-origin `/assets/` and `/fonts/` resources. It validates Workslip's push handler, notification creation, click handler, router acknowledgement, fallback boundary, and cache isolation. It does not validate the operating-system notification tray or the external push provider transport.

`all-critical` expands into eleven independent GitHub Actions matrix jobs with `max-parallel: 4`. Each flow has its own browser process, report, screenshots where the scenario permits them, cleanup, result, and artifact. `public-smoke` remains a write-free availability check.

`src/FE/scripts/playwright-release-runner.mjs` is the required entry point. It rejects authenticated scenarios when the resolved target does not permit release-test access. Do not bypass it by invoking the underlying scenario orchestrator directly.

## Runtime optimization

The workflow uses Microsoft's version-matched `mcr.microsoft.com/playwright:v1.55.0-noble` image. Chromium and its Linux system dependencies are already in that image, so the job does not run `playwright install --with-deps` on every invocation.

Only the isolated `playwright@1.55.0` Node runtime is installed under `src/FE/scripts/node_modules`. The full frontend dependency graph is not installed for a deployed smoke run. The per-flow timeout is 35 minutes.

## Data and contract rules

The suite must not depend on pre-existing IDs, customers, jobs, users, reference-data values, addresses, or sort order.

- Runtime API contracts are loaded from the deployed `/openapi/v1.json` while release-test endpoints are enabled.
- Executable request examples and unique-value conventions are loaded from `Postman/postman_collection.json`.
- Installation types, users, customers, jobs, roles, and organization context are loaded from runtime API responses.
- Postal addresses are selected from the DAWA autocomplete service and entered through the real UI control.
- Missing assignable users and isolated tenant fixtures are created through documented API contracts using unique values derived from the Postman collection.
- User-visible state transitions are performed through the actual UI. Direct API calls are limited to fixture discovery/setup, assertions, tenant-boundary probes, and cleanup.

Every direct API call must exist in both runtime OpenAPI and the Postman collection. Missing contract coverage fails the scenario instead of falling back to guessed data or request shapes.

## Test-data lifecycle

Jobs, customers, worksheets, and users created by a scenario are removed where a delete contract exists. Organization and invitation fixtures are retained and clearly prefixed because the current public contract has no corresponding delete operation. Retained and failed-cleanup fixtures are listed in `report.json`.

All generated fixtures include a `PLAYWRIGHT` marker and unique run identifier. Full flows are permitted only when the resolved target explicitly enables destructive Playwright and contains no customer production data.

Before customer go-live, retained fixtures and cleanup failures must be reviewed and removed or intentionally isolated. After go-live, no full flow may target production even if an operator selects it manually; the policy resolver and release runner block the attempt.

## Authentication and sensitive evidence

The authenticated suite uses deployed dev-login controls. The scenarios fail when those controls or the dev-token endpoint are unavailable; they must not silently switch to embedded credentials or assumed users. Tokens are kept in memory and are never written to artifacts.

Authenticated Playwright traces are not uploaded because they can contain authorization headers, request bodies, and personal data. Artifacts contain redacted JSON reports and selected screenshots. Login steps do not take screenshots. The `notification-navigation` scenario uploads only its redacted JSON report and does not capture authenticated screenshots.

The invitation scenario verifies the real UI through the Microsoft handoff. Completing Microsoft enrollment requires an isolated third-party identity session and is reported as a coverage limitation when no such session is available.

## Known product gap

The current rejection dialog and `ChangeJobStatusRequest` do not contain a rejection-reason field. The `rejection-loop` scenario verifies the status transition, correction, resubmission, approval, and history, but it cannot verify a reason that the product does not currently store. The product correction is tracked in WOR-292.

## Local Windows validation

Use `tools/playwright/run-critical-local.ps1` from the repository root. It resolves the selected target from the committed release policy and refuses unsafe scenario/target combinations.

### Fast direct run

Prerequisite: Node.js 20 or newer; Node.js 22 is recommended.

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Direct `
  -Target Production `
  -Scenario public-smoke
```

During the documented pre-live phase, an authenticated production flow is allowed:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Direct `
  -Target Production `
  -Scenario notification-navigation
```

After the live switch, the same command is rejected. Use `-Target Staging` for authenticated/full flows.

### Actual YAML through `act`

Prerequisites:

- Docker Desktop is installed and running.
- `act` is installed.

```powershell
winget install Docker.DockerDesktop
winget install nektos.act
```

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Workflow `
  -Target Production `
  -Scenario public-smoke
```

The helper creates the `workflow_dispatch` event JSON in the temporary directory, pre-pulls the version-matched Playwright image when necessary, calls `act`, removes the event file, and opens `artifacts/playwright-prod-smoke`.

`act` sets `ACT=true`. The workflow therefore skips GitHub's artifact-upload action locally while preserving screenshots and `report.json` in the mounted repository workspace. Local evidence is ignored by Git.

A successful `act` run proves that the workflow can execute in local Docker emulation. It does not prove that GitHub-hosted runner permissions, network access, or the target environment are identical.

## GitHub Actions run

Open **Actions → Playwright critical flows → Run workflow**, choose both target and scenario, and run it from the default branch.

A single scenario produces `playwright-critical-flows-<target>-<scenario>`. `all-critical` produces one artifact per scenario, retained for seven days.

A passing workflow is evidence only for the selected scenario, deployed revision, resolved environment, browser, and viewport recorded in the artifact.
