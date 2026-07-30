# Workslip Playwright E2E

**Status:** Active  
**Owner:** Frontend  
**Source of truth:** `playwright.config.ts`, executable tests, GitHub workflow results, and retained Playwright artifacts  
**Review cadence:** When authentication, routing, CI, or critical user flows change

## Purpose

This package contains browser validation for Workslip. It is isolated from the production frontend dependency graph so Playwright and browser binaries do not affect the application bundle or the main frontend lockfile.

Code inspection, linting, TypeScript, and production builds remain required, but they do not replace browser interaction. User-visible frontend work is not validation-complete until the relevant Playwright scenario has exercised the actual controls in a running application.

The harness has two layers:

1. **Pull-request browser validation** uses the real React application with deterministic API responses. It runs automatically in desktop and mobile Chromium without credentials or external mutations.
2. **Live authenticated validation** performs the real Workslip OTP flow against a selected deployed environment using a dedicated non-production Mailosaur user.

The deterministic layer validates frontend behavior and browser integration. It does not replace the live authentication and backend integration smoke.

## Install

From `src/FE`:

```bash
npm run test:e2e:install
```

This installs the isolated `e2e` package and Chromium. On Linux CI, use the package's `install:browsers:ci` script so required system libraries are installed as well.

## Pull-request browser suite

```bash
npm run test:e2e:pr
```

Without `E2E_BASE_URL`, Playwright starts the local Vite frontend on `http://127.0.0.1:5270`. The suite runs on:

- desktop Chromium at 1440 × 1000;
- a Pixel 7 mobile viewport.

### Public login scenario

```bash
npm run test:e2e:public
```

The public scenario intercepts only the one-time-code send request needed to exercise the UI transition deterministically. It verifies:

- the Microsoft passkey login surface;
- the lazy-loaded OTP form;
- native invalid-email behavior;
- the email-to-code transition;
- invalid-code validation;
- navigation back to passkey login.

### Authenticated UI scenario

The pull-request suite also loads the real authenticated React application with a test token and controlled `/api/*` responses. It verifies in desktop and mobile Chromium:

- the authenticated application shell;
- the Sager route and create action;
- the create bottom sheet;
- navigation to the simple-job form;
- the disabled initial submit state;
- return navigation;
- logout back to the login route.

Unexpected real `/api/*` calls are rejected so newly introduced dependencies cannot silently escape the deterministic test boundary.

## Live authenticated OTP smoke

The live authenticated suite performs the real Workslip one-time-code login and requires a dedicated non-production Workslip user whose mailbox is hosted in Mailosaur.

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

The live smoke runs once in desktop Chromium to avoid sending duplicate OTP emails. Automatic mobile coverage is provided by the deterministic authenticated UI scenario. Mobile-specific live integration changes must add a targeted mobile scenario.

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

The tests do not use `/api/dev/token` against deployed environments. Live CI authentication uses the actual OTP flow and repository secrets.

## GitHub Actions

`.github/workflows/playwright-e2e.yml` provides two focused jobs:

- **Pull request browser smoke** runs automatically when `src/FE/**` or the workflow changes. It generates the API client, builds the frontend, installs Chromium, and executes public and deterministic authenticated scenarios in desktop and mobile projects.
- **Live authenticated OTP smoke** is manual because it targets a selected deployed URL and consumes protected Mailosaur secrets.

Configure these repository secrets before running the live authenticated job:

```text
E2E_EMAIL
E2E_MAILOSAUR_API_KEY
E2E_MAILOSAUR_SERVER_ID
```

The manual workflow must run from a trusted default-branch workflow definition. This prevents a feature branch from changing the workflow to exfiltrate repository secrets.

After the workflow is merged, use **Actions → Playwright E2E → Run workflow**, select `authenticated` or `all`, and provide the deployed branch or isolated environment URL. A production-connected run must remain non-destructive.

## Adding feature coverage

A feature PR must add or update a scenario that:

- reaches the feature through normal user navigation;
- operates the actual changed controls;
- verifies the relevant visible success, failure, loading, disabled, and recovery behavior;
- checks console and API diagnostics through the shared fixture;
- uses a mobile project when the behavior is mobile-sensitive;
- avoids destructive production mutations.

Mocked browser validation must reject unrecognized API dependencies rather than silently returning generic success. High-risk authentication, tenant, and integration changes must also run the live or isolated integration layer.

A page-load-only test is not sufficient evidence.
