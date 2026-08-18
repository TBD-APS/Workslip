# Playwright validation policy

**Status:** Active — authenticated PR-CI uses an ephemeral localhost full stack; production remains read-only smoke only

**Owner:** Workslip release maintainers

**Source of truth:** `.github/workflows/frontend-validation.yml`, `src/FE/scripts/run-playwright-ephemeral.sh`, `src/FE/scripts/playwright-ephemeral-auth.mjs`, `src/FE/config/release-environments.json`, `tools/release/resolve-release-environment.mjs`, `.github/workflows/playwright-prod-smoke.yml`, and run evidence

**Review cadence:** Before changing a browser-test target, authentication method, or destructive scenario

## Validation lanes

Workslip deliberately separates **PR regression evidence** from **deployed production smoke evidence**.

### PR-CI authenticated browser regression

Every non-documentation change runs `Playwright integration (ephemeral)` as a blocking `CI Gate` dependency.

The job creates a disposable full stack from the exact checked-out revision:

- Vite frontend on loopback;
- ASP.NET Core API on loopback with `ASPNETCORE_ENVIRONMENT=Development`;
- disposable SQL Server database;
- development seed data only;
- per-run SQL password and JWT signing key;
- headless Chromium.

The browser receives a synthetic Development JWT through the existing `/api/dev/token` endpoint, stores it through the same `localStorage.authToken` contract used by the frontend, and then boots the normal authenticated React/API path. `/api/auth/me` must succeed before a UI assertion is accepted.

This is **not a deployed authentication mechanism**. `playwright-ephemeral-auth.mjs` rejects any app or API origin that is not `localhost`, `127.0.0.1`, or `::1`, and the backend endpoint remains available only in ASP.NET Development. A production, Vercel, arbitrary remote, disguised-loopback, credentialed, or path-bearing URL fails before a token request is made.

The blocking browser regression proves:

- authenticated Admin bootstrap against the disposable API/database;
- iPhone viewport hides Quick Navigator desktop keyboard hints (`Esc`, `↑/↓`, `Enter` footer);
- desktop `Ctrl+K` opens Quick Navigator and the keyboard hints remain visible;
- Overview status cards deep-link to the matching job filter, survive reload and restore on back navigation.

The Overview scenario is the `overview-navigation` flow that [`tools/release/validate-pr-browser-evidence.mjs`](../../tools/release/validate-pr-browser-evidence.mjs) infers for any change touching Overview. Running it here means the evidence the guard demands is produced by CI rather than by hand. It asserts navigation and filter state rather than job counts, so it holds on the disposable database's seed data.

The guard also infers a `job-wizard` flow for changes under `src/FE/src/features/jobs/`. No ephemeral scenario covers that path yet, so those changes still require a manual run. Closing that gap means porting the `/app/job/new` coverage in `playwright-critical-domain.mjs` onto the ephemeral auth contract; until then, do not treat a green CI Gate as job-wizard evidence.

Backend/frontend process logs are uploaded only on failure. Browser storage state, bearer tokens, traces, customer data, and authenticated screenshots are not uploaded.

### Production Playwright

The deployed production lane remains only:

- `public-smoke` against `https://app.mrsoftware.dk`.

It opens the public application in Chromium at the iPhone 13 viewport and verifies that the page responds. It does not authenticate, send an OTC, create data, or invoke an API mutation.

`src/FE/config/release-environments.json` remains fail-closed:

- production has `enableDevelopmentEndpoints: false` and `allowDestructivePlaywright: false`;
- staging has no URL and is not a runnable target;
- `playwright-release-runner.mjs` rejects every non-public scenario against production.

The production workflow exposes no destructive/authenticated fallback. A failed or unavailable PR browser lane must never be replaced by mutations against production.

## Maintained critical suite

The broader critical scenario harness remains in the repository for focused release/local validation and future expansion. It includes auth/session, KLS lifecycle, rejection, draft recovery, tenant/role isolation, invitation onboarding, assignment lifecycle, customer lifecycle, worksheet integrity and diverse lifecycle scenarios.

Those scenarios are distinct from the small blocking PR smoke. Do not automatically move every critical scenario into every PR: add browser-level coverage where the regression risk justifies its runtime and fixture cost.

The critical/release harness still models deployed authentication through the normal one-time-code UI. There is no approved automated inbox reader today, so its non-interactive deployed authenticated mode remains fail-closed. An explicitly opted-in local headed/TTY run can use operator-entered OTC where policy permits.

The new ephemeral lane does not weaken that rule. It removes mailbox delivery as a prerequisite for generic frontend → API → database regression by using the already-existing Development-only synthetic token boundary on localhost.

## Assignment duplication coverage

`assignment-lifecycle` models the WOR-424 requirement rather than fabricating random users:

1. an Admin resolves the stable configured `User` and `Admin` identities in the active test organization;
2. the Admin creates a KLS task with **Opret en kopi af sagen til hver medarbejder** enabled;
3. the test proves there is exactly one independent copy per assignee;
4. each assignee completes and submits only their own copy;
5. the Admin approves both copies individually.

The harness never creates identities just to make a scenario pass. If configured identities are missing or have unexpected roles, the scenario stops without exposing their addresses.

## Authentication and evidence boundaries

The real Workslip login contract remains:

`/api/auth/send-code` → `/api/auth/verify-code/{code}` → `/api/auth/me`.

The ephemeral PR lane tests authenticated application behavior with the Development-only synthetic token and still requires the normal frontend session bootstrap plus `/api/auth/me`. It does **not** claim to test email delivery or OTC verification.

For deployed/OTC testing, the maintained critical harness uses the configured role identities described in `synthetic-test-identities.md`. No Graph/shared-mailbox provider or static application-token fallback is introduced by the ephemeral lane.

Artifacts stay minimized:

- production public smoke may save a screenshot;
- authenticated critical scenarios retain no screenshots while redaction is insufficient for identity/customer data;
- ephemeral authenticated PR smoke uploads no browser screenshots, traces, bearer tokens or storage state;
- reports/logs must redact OTC values, bearer tokens, query secrets and personal identifiers;
- disposable PR data lives only in the ephemeral SQL container.

## Security boundary for Development auth

`/api/dev/token` is acceptable for the PR browser lane only when all of these are true:

1. frontend target is loopback;
2. API target is loopback;
3. API runs as ASP.NET Development;
4. database is disposable and synthetic-only;
5. credentials are generated per run and masked;
6. no token/storage artifact is retained.

Breaking any target-origin rule must fail before `/api/dev/token` is called.

Do not add `/api/dev/token` to Production/Staging endpoint maps. Do not make the browser helper accept remote hosts through configuration. Do not add a mailbox/provider integration merely to support generic browser regression.

## Local Windows validation

The existing release helper remains available for the production public smoke:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Direct `
  -Target Production `
  -Scenario public-smoke
```

It installs the version-matched Playwright runtime when required, validates release policy/source tests, resolves the committed target, and writes evidence under `artifacts/playwright-prod-smoke`.

If an operator network requires an outbound HTTP(S) proxy, `WORKSLIP_PLAYWRIGHT_PROXY` may contain only a credential-free proxy origin. A local operator may additionally set `WORKSLIP_PLAYWRIGHT_IGNORE_HTTPS_ERRORS=true` only together with that explicit proxy when TLS interception prevents Chromium trust. Such a run proves browser flow/connectivity, not the target certificate chain.

`-Mode Workflow` intentionally supports only the public production smoke. A destructive critical scenario or staging target is rejected before Docker/`act` work begins.

The ephemeral authenticated runner is Linux/CI-oriented because it owns a disposable SQL Server container and local API/frontend processes. Do not point it at a deployed URL.

## CI ownership

For non-documentation changes, `CI Gate` requires:

- Backend;
- Frontend + API contract;
- Contracts + docs;
- Postman integration (ephemeral);
- Playwright integration (ephemeral).

The contracts job syntax-checks the browser scripts/shell runner and runs focused tests for the loopback-only auth boundary. Green source tests do not replace the real Chromium job; both are required.

## Future expansion

Additional authenticated browser scenarios should reuse the ephemeral lane when they need real frontend → API → SQL behavior. Add scenarios only where browser-level regression protection is valuable; do not duplicate unit/API coverage for trivial mappings.

A separately deployed staging environment may still be justified later for provider callbacks, networking, deployment-specific behavior, true release-candidate infrastructure or production-like identity delivery. It is no longer a prerequisite for ordinary authenticated browser regression.
