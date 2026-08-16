# Playwright validation policy

**Status:** Active — authenticated PR-CI uses an ephemeral localhost full stack; production remains read-only smoke only

**Owner:** Workslip release maintainers

**Source of truth:** `.github/workflows/frontend-validation.yml`, `src/FE/scripts/run-playwright-ephemeral.sh`, `src/FE/scripts/playwright-ephemeral-auth.mjs`, `src/FE/config/release-environments.json`, `.github/workflows/playwright-prod-smoke.yml`, and run evidence

**Review cadence:** Before changing a browser-test target, authentication method, or destructive scenario

## Two separate browser-test lanes

Workslip deliberately separates **PR regression evidence** from **production smoke evidence**.

### 1. PR-CI authenticated browser regression

Every non-documentation change runs `Playwright integration (ephemeral)` as a blocking `CI Gate` dependency.

The job creates a disposable full stack from the exact checked-out SHA:

- Vite frontend on loopback;
- ASP.NET Core API on loopback with `ASPNETCORE_ENVIRONMENT=Development`;
- disposable SQL Server database;
- development seed data only;
- a per-run SQL password and JWT signing key;
- headless Chromium.

The browser receives a synthetic Development JWT through the existing `/api/dev/token` endpoint, stores it through the same `localStorage.authToken` contract used by the frontend, and then boots the normal authenticated React/API path. `/api/auth/me` must succeed before the UI assertion is accepted.

This is **not a deployed authentication mechanism**. `playwright-ephemeral-auth.mjs` rejects any app or API origin that is not `localhost`, `127.0.0.1`, or `::1`, and the backend endpoint remains available only in ASP.NET Development. A production, Vercel, staging, arbitrary remote, disguised-loopback, credentialed, or path-bearing URL fails before a token request is made.

The initial blocking browser regression proves:

- authenticated browser bootstrap against the disposable API/database;
- the iPhone viewport hides Quick Navigator desktop keyboard hints (`Esc`, `↑/↓`, `Enter` footer);
- desktop keeps those keyboard hints visible.

Backend/frontend process logs are uploaded only on failure. Browser storage state, bearer tokens, traces, customer data, and authenticated screenshots are not uploaded.

## 2. Production Playwright

Production remains restricted to the write-free `public-smoke` against `https://app.mrsoftware.dk`.

`src/FE/config/release-environments.json` keeps:

- `enableDevelopmentEndpoints: false` for production;
- `allowDestructivePlaywright: false` for production;
- no runnable deployed staging target unless separately approved later.

The production workflow authenticates nobody, sends no OTC, creates no data, and invokes no mutation. A destructive/authenticated scenario must never be redirected to production as a fallback.

## OTC authentication coverage

The real Workslip login contract remains:

`/api/auth/send-code` → `/api/auth/verify-code/{code}` → `/api/auth/me`.

Generic authenticated UI regression no longer depends on reading a mailbox in CI. The ephemeral lane tests application authorization/session behavior through a Development-only synthetic token boundary, while the deployed OTC flow remains a separate auth concern.

The maintained critical/release harness may still use interactive OTC for explicitly approved headed local runs. There is no automatic mailbox reader, Graph integration, Mailosaur dependency, static production token, or hidden deployed auth bypass.

## Security boundary

The Development token is acceptable only when all of these are true:

1. frontend target is loopback;
2. API target is loopback;
3. API runs as ASP.NET Development;
4. database is disposable and synthetic-only;
5. credentials are generated per run and masked;
6. no token/storage artifact is retained.

Breaking any target-origin rule must fail before `/api/dev/token` is called.

Do not add `/api/dev/token` to Production/Staging endpoint maps. Do not make the browser harness accept remote hosts through configuration. Do not introduce a mailbox/provider integration merely to support generic browser regression.

## CI ownership

`CI Gate` requires, for non-documentation changes:

- Backend;
- Frontend + API contract;
- Contracts + docs;
- Postman integration (ephemeral);
- Playwright integration (ephemeral).

The contracts job syntax-checks the browser scripts and shell runner and runs focused tests for the loopback-only auth boundary. A green source test is not a substitute for the real Chromium job; both are required.

## Future expansion

Additional authenticated browser scenarios should reuse the ephemeral lane when they need real frontend → API → SQL behavior. Add scenarios only where browser-level regression protection is valuable; do not duplicate unit/API coverage for trivial mappings.

A separately deployed staging environment may still be justified later for provider callbacks, networking, deployment-specific behavior, or full release-candidate testing. It is no longer a prerequisite for ordinary authenticated browser regression.
