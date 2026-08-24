# Playwright validation policy

**Status:** Active — authenticated PR-CI uses an ephemeral localhost full stack; production remains read-only smoke only

**Owner:** Workslip release maintainers

**Source of truth:** `.github/workflows/frontend-validation.yml`, `src/FE/scripts/run-playwright-ephemeral.sh`, `src/FE/scripts/playwright-ephemeral-auth.mjs`, `src/FE/config/release-environments.json`, `tools/release/resolve-release-environment.mjs`, `.github/workflows/playwright-prod-smoke.yml`, and run evidence

**Review cadence:** Before changing a browser-test target, authentication method, destructive scenario, fixture boundary, or browser-evidence gate

## Validation lanes

Workslip deliberately separates **implementation iteration**, **PR regression evidence** and **deployed production smoke evidence**.

### Draft implementation lane

A draft pull request is the implementation lane. Non-documentation changes still run deterministic build/test checks and the ephemeral Postman/API boundary, but the expensive authenticated Playwright job is skipped while the PR remains draft.

Before leaving draft, complete the implementation/testability review:

- the product change is cohesive and no known implementation edits remain;
- browser interaction points already expose stable DOM IDs;
- focused Unit/API regressions are in place where justified;
- Playwright prerequisite data can be created deterministically without unrelated UI setup;
- required browser flows, registered scripts and viewports are declared in the PR body.

`Ready for review` is the browser-evidence code-freeze transition. It triggers the authenticated browser job on the current head. A later commit to a ready PR triggers fresh exact-head evidence. If implementation must resume after browser evidence starts, normally convert the PR back to draft first.

The merge-gating runtime result is the exact-head `CI Gate`, not a manually maintained `Browser-Result` field in the PR description. Draft implementation validation uses the separate `Draft CI Gate` check context so it cannot be reused as merge evidence after the Ready transition.

### PR-CI authenticated browser regression

For a ready non-documentation pull request, and for code pushes to `main`/release branches, `Playwright integration (ephemeral)` is a blocking `CI Gate` dependency.

The job creates a disposable full stack from the exact checked-out revision:

- Vite frontend on loopback;
- ASP.NET Core API on loopback with `ASPNETCORE_ENVIRONMENT=Development`;
- disposable SQL Server database;
- development seed data only;
- per-run SQL password and JWT signing key;
- headless Chromium.

The browser receives a synthetic Development JWT through the existing `/api/dev/token` endpoint, stores it through the same `localStorage.authToken` contract used by the frontend, and then boots the normal authenticated React/API path. `/api/auth/me` must succeed before authenticated UI evidence is accepted.

This is **not a deployed authentication mechanism**. `playwright-ephemeral-auth.mjs` rejects any app or API origin that is not `localhost`, `127.0.0.1`, or `::1`, and the backend endpoint remains available only in ASP.NET Development. A production, Vercel, arbitrary remote, disguised-loopback, credentialed, or path-bearing URL fails before a token request is made.

The maintained blocking runner validates the suite stability policy before starting the disposable runtime and then executes the registered authenticated scenarios. `tools/release/validate-pr-browser-evidence.mjs` classifies changed UI paths into flow names. The PR must declare the matching flows, bind every flow to a concrete `playwright-*.mjs` script in `Browser-Scripts`, and declare required viewports. Feature Change Guard verifies those scripts are registered through `run_scenario` in the exact-head `run-playwright-ephemeral.sh`; the exact-head browser job then supplies runtime pass/failure truth.

This prevents a generic green browser suite from being treated as proof for a named flow that the runner never exercised. If a feature needs a focused browser scenario that is not registered, add the scenario and register it before the PR becomes ready.

Backend/frontend process logs are uploaded only on failure. Browser storage state, bearer tokens, traces, customer data, and authenticated screenshots are not uploaded.

### Deterministic fixture boundary

Browser scenarios should spend browser time on the user behavior they claim to prove, not on manufacturing prerequisites.

Default fixture rule:

1. create synthetic prerequisite entities through the existing Development-only API/seed boundary;
2. verify prerequisite/persisted state through authoritative API reads when useful;
3. enter the UI at the closest stable point before the behavior under test;
4. perform the changed click/form/navigation/state transition through the real UI;
5. assert the resulting user-visible behavior and/or authoritative persisted result.

Do not click through unrelated create/edit flows merely to construct a fixture. A failure in an unrelated setup surface must not prevent the target scenario from starting or create a misleading timeout. Use UI fixture creation only when fixture creation itself is the changed journey under test.

This rule does not permit API calls to replace the user action being validated. For example, API may create a synthetic Draft job and later read its status, while the actual complete/submit/reject/correct/resubmit transitions remain browser actions when that lifecycle is the regression under test.

### Stable selector boundary

Product UI interaction and synchronization in Playwright use stable DOM IDs. Visible copy, translations, placeholders, labels, accessible names, `data-testid` selectors, generated classes and array positions are not the browser selector contract.

`Feature change guard` ratchets this rule for new Playwright code: newly added selector lines are rejected when they introduce `getBy*` copy/accessibility/test-id selectors or literal non-ID locators. Existing legacy selectors remain migration debt and must not be copied into new work.

Stable IDs must be designed into the component while the UI is implemented, before the browser scenario is written. Do not complete a browser script and then retrofit product IDs after the first failure.

### Production Playwright

The deployed production lane remains only:

- `public-smoke` against `https://app.mrsoftware.dk`.

It opens the public application in Chromium at the maintained mobile viewport and verifies that the page responds. It does not authenticate, send an OTC, create data, or invoke an API mutation.

`src/FE/config/release-environments.json` remains fail-closed:

- production has `enableDevelopmentEndpoints: false` and `allowDestructivePlaywright: false`;
- staging has no URL and is not a runnable target;
- `playwright-release-runner.mjs` rejects every non-public scenario against production.

The production workflow exposes no destructive/authenticated fallback. A failed or unavailable PR browser lane must never be replaced by mutations against production.

## Maintained critical suite

The broader critical scenario harness remains in the repository for blocking/focused release/local validation and future expansion. It includes auth/session, KLS lifecycle, rejection, draft recovery, tenant/role isolation, invitation onboarding, assignment lifecycle, customer lifecycle, worksheet integrity and diverse lifecycle scenarios.

Do not automatically create another browser scenario because a frontend component changed. Add browser-level coverage where the regression lives in the browser experience; use Unit/Postman for risks those layers prove more directly.

The critical/release harness still models deployed authentication through the normal one-time-code UI. There is no approved automated inbox reader today, so its non-interactive deployed authenticated mode remains fail-closed. An explicitly opted-in local headed/TTY run can use operator-entered OTC where policy permits.

The ephemeral lane does not weaken that rule. It removes mailbox delivery as a prerequisite for generic frontend → API → database regression by using the already-existing Development-only synthetic token boundary on localhost.

## Worksheet admin overview (`worksheet`)

`playwright-power-bi-admin-overview.mjs` validates the Timer admin view for an authenticated Admin user.

The scenario exercises:

- navigation to `/app/timer`;
- the Power BI report config endpoint `/api/worksheets/all/report/power-bi` returning HTTP 200;
- conditional visibility of the Power BI section based on the config response.

When the config response contains a `url`, the scenario asserts:

- `#timer-power-bi-report` is visible;
- `#power-bi-report-title` is visible;
- if `embedUrl` is present, `#timer-power-bi-frame` is visible and its `src` begins with `https://app.powerbi.com/reportEmbed?`;
- if `embedUrl` is absent, no iframe is rendered and the UI explains that the configured report cannot be embedded securely.

When the config response contains no `url`, the scenario asserts:

- `#timer-power-bi-report` is absent from the product UI;
- `#timer-power-bi-frame` is not rendered.

The scenario also asserts no horizontal page overflow.

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

For a draft code PR, `Draft CI Gate` requires:

- Backend;
- Frontend + API contract;
- Contracts + docs;
- Postman integration (ephemeral);
- Playwright may be `skipped` by design.

For a ready code PR or a code push to a protected delivery branch, the separate merge-required `CI Gate` additionally requires `Playwright integration (ephemeral)` to succeed on that exact workflow revision. For UI runtime PRs, Feature Change Guard has already verified that every inferred browser flow maps to a script registered in that exact-head runner.

The contracts job syntax-checks browser scripts/shell runners and runs focused source tests. Green source tests do not replace required Chromium evidence on a ready revision.

## Future expansion

Additional authenticated browser scenarios should reuse the ephemeral lane when they need real frontend → API → SQL behavior. Add scenarios only where browser-level regression protection is valuable; do not duplicate unit/API coverage for trivial mappings.

A separately deployed staging environment may still be justified later for provider callbacks, networking, deployment-specific behavior, true release-candidate infrastructure or production-like identity delivery. It is no longer a prerequisite for ordinary authenticated browser regression.
