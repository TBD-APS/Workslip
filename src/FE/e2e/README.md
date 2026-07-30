# Workslip Playwright E2E

**Status:** Active  
**Owner:** Frontend  
**Source of truth:** `playwright.config.ts`, executable tests, GitHub workflow results, and retained Playwright artifacts  
**Review cadence:** When authentication, routing, CI, or critical user flows change

## Purpose

This package contains browser validation for Workslip. It is isolated from the production frontend dependency graph so Playwright and browser binaries do not affect the application bundle or the main frontend lockfile.

Code inspection, linting, TypeScript, and production builds remain required, but they do not replace browser interaction. User-visible frontend work is not validation-complete until the relevant Playwright scenario has exercised the actual controls in a running application.

## Install

From `src/FE`:

```bash
npm run test:e2e:install
```

This installs the isolated `e2e` package and Chromium. On Linux CI, use the package's `install:browsers:ci` script so required system libraries are installed as well.

## Public smoke

```bash
npm run test:e2e:public
```

Without `E2E_BASE_URL`, Playwright starts the local Vite frontend on `http://127.0.0.1:5270`. The public suite does not require a backend. It intercepts only the one-time-code send request needed to exercise the UI transition deterministically.

The public suite runs on:

- desktop Chromium;
- a Pixel 7 mobile viewport.

It verifies the passkey login surface, lazy OTP form, invalid-email state, code-step transition, invalid-code state, and return navigation.

## Authenticated smoke

The authenticated suite performs the real Workslip one-time-code login and therefore requires a dedicated non-production Workslip user whose mailbox is hosted in Mailosaur.

Required environment variables:

```text
E2E_BASE_URL
E2E_EMAIL
E2E_MAILOSAUR_API_KEY
E2E_MAILOSAUR_SERVER_ID
```

Run:

```bash
npm run test:e2e:authenticated
```

The suite:

1. opens the normal Workslip login route;
2. requests a one-time code through the real UI;
3. retrieves the new message from Mailosaur without printing its contents;
4. enters the six-digit code through the real control;
5. verifies authenticated navigation;
6. opens the create sheet and the simple-job form;
7. verifies the form cannot submit without hours;
8. returns without creating data;
9. logs out and verifies the login screen.

The authenticated smoke currently runs once in desktop Chromium to avoid sending duplicate OTP emails. Mobile coverage is provided by the deterministic public suite; mobile-specific authenticated changes must add a targeted mobile scenario.

## Browser diagnostics

The auto fixture fails tests on:

- unhandled page exceptions;
- browser console errors;
- failed `/api/*` requests;
- `/api/*` responses with status `400` or higher.

A justified known condition may be allow-listed through comma- or newline-separated substrings:

```text
E2E_ALLOWED_CONSOLE_ERRORS
E2E_ALLOWED_API_FAILURES
```

Allow-list entries must be narrow and documented in the PR. They must not be used to conceal a regression.

## Evidence and secret handling

Playwright retains traces, screenshots, and videos only for failures. GitHub Actions uploads `playwright-report` and `test-results` for seven days.

Never commit or attach:

- Mailosaur API keys;
- Workslip JWTs;
- authenticated storage state;
- mailbox message bodies;
- personal customer or employee data.

The tests do not use `/api/dev/token` against deployed environments. CI authentication uses the actual OTP flow and repository secrets.

## GitHub Actions

`.github/workflows/playwright-e2e.yml` provides two focused jobs:

- **Public browser smoke** runs on pull requests that change `src/FE/**` or the workflow itself. It builds the frontend and runs the local public suite.
- **Authenticated OTP smoke** is manual because it targets a selected deployed URL and consumes protected Mailosaur secrets.

Configure these repository secrets before running the authenticated job:

```text
E2E_EMAIL
E2E_MAILOSAUR_API_KEY
E2E_MAILOSAUR_SERVER_ID
```

Use **Actions → Playwright E2E → Run workflow**, select `authenticated` or `all`, and provide the deployed branch or environment URL. A production-connected preview must use only non-destructive scenarios.

## Adding feature coverage

A feature PR must add or update a scenario that:

- reaches the feature through normal user navigation;
- operates the actual changed controls;
- verifies the relevant visible success, failure, loading, disabled, and recovery behavior;
- checks console and API diagnostics through the shared fixture;
- uses a mobile project when the behavior is mobile-sensitive;
- avoids destructive production mutations.

A page-load-only test is not sufficient evidence.
