# Synthetic test identities

**Status:** Temporary; automated authenticated execution is blocked until an approved inbox reader exists

**Owner:** Workslip product and release maintainers

**Source of truth:** GitHub environment variables, deployed `/api/auth` behavior, and Playwright run evidence

**Review cadence:** Before every authenticated release-test run and when the identity or inbox strategy changes

## Purpose

Authenticated Playwright release tests temporarily use four existing non-production Workslip users and the normal one-time-code authentication flow. The historical `WORKSLIP_SYNTHETIC_*_EMAIL` names are retained so operations can replace an address without a source change.

The actual addresses are personal configuration data. Keep them in the approved GitHub environment or the operator's local process environment, never in repository files, reports, screenshots, command examples, or logs.

## Identity model

Configure exactly one stable identity for each role:

- `WORKSLIP_SYNTHETIC_USER_EMAIL`
- `WORKSLIP_SYNTHETIC_AUDITOR_EMAIL`
- `WORKSLIP_SYNTHETIC_ADMIN_EMAIL`
- `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL`

These users already exist. This harness does not create users, mutate their roles, or accept a privileged setup token. The Auditor identity may be a disposable test-role account. A missing variable fails with the variable name, and a mismatch between the configured role and `/api/auth/me` rejects the login without logging the address.

The Admin inbox must receive any derived plus-address used by scenarios that create an isolated secondary organization. Verify that behavior manually before running `role-tenant-isolation`.

## Authentication flow

`playwright-prod-smoke.mjs` drives the real deployed UI:

1. Open `/login`.
2. Select the one-time-code login.
3. Submit the synthetic email through `/api/auth/send-code`.
4. The local operator reads the delivered code and enters it directly in the visible Workslip browser field.
5. The page submits `/api/auth/verify-code/{code}` and stores the normal short-lived application JWT.
6. The harness calls `/api/auth/me` with that token and verifies the expected role.

The Node harness never reads the code field or verify URL. Login screenshots mask the OTC field, and the email address and code must not be written to console output or retained artifacts.

## Fail-closed automation boundary

There is currently no approved automated inbox reader for these addresses. Therefore:

- `public-smoke` runs without identity configuration and sends no authentication mail;
- every authenticated non-interactive run fails at startup before `/api/auth/send-code`;
- GitHub Actions does not enable interactive mode and cannot run authenticated scenarios successfully;
- there is no fallback authentication path, durable application token, or privileged identity setup path.

This boundary is intentional. Adding mailbox credentials, a provider API, a browser session, another external processor, or a CI authentication grant requires explicit approval and a separate security/privacy review.

## Explicit local interactive run

An operator who can access all inboxes needed by the selected scenario may run it locally from an interactive terminal. Set the four role variables without printing their values, then opt in:

```powershell
$env:WORKSLIP_PLAYWRIGHT_INTERACTIVE_OTC = 'true'
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Direct `
  -Target Production `
  -Scenario auth-session
Remove-Item Env:WORKSLIP_PLAYWRIGHT_INTERACTIVE_OTC
```

The harness requires both the exact opt-in value `true` and a TTY, and launches Chromium headed. For each login, enter the delivered code only in the browser and submit the visible form. Do not paste a code into the terminal. Target safety rules in `src/FE/config/release-environments.json` still apply.

## Data and security rules

The four identities must be limited to the designated non-production/release-test context and keep one stable Workslip role each. Test-generated customers, jobs, users, and worksheets follow the existing Playwright cleanup policy; these existing identities are not deleted by scenario cleanup.

Email addresses and authentication artifacts are personal/security data. Limit access to repository/environment maintainers, do not expose them in source or artifacts, and follow the approved retention and access policy for GitHub variables and local shell history. This document records technical minimization controls, not proof of legal compliance.

## Remaining validation and merge boundary

The helper's source tests cover the deterministic pre-send failure, the explicit headed/TTY gate, and OTC URL redaction. They do not prove deployed email delivery or OTC login.

Before PR #403 can leave draft, run an authenticated scenario against the deployed release-test target, verify the real send and verify endpoints plus `/api/auth/me`, record browser/viewport and scenario outcome without addresses or codes, and confirm the resulting report, screenshots, console output, and network-failure summary contain no sensitive values. Until then the authenticated suite is **implemented but Playwright-unvalidated**.
