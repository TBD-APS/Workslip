# Playwright release-test policy

**Status:** Partially configured — only the write-free production smoke is runnable today

**Owner:** Workslip release maintainers

**Source of truth:** `src/FE/config/release-environments.json`, `tools/release/resolve-release-environment.mjs`, `.github/workflows/playwright-prod-smoke.yml`, and run artifacts

**Review cadence:** Before changing a release target, authentication method, or destructive scenario

## Current runnable scope

The only deployed Playwright run that is configured and allowed is:

- `public-smoke` against `https://app.mrsoftware.dk`.

It opens the public application in Chromium at the iPhone 13 viewport and verifies that the page responds. It does not authenticate, send an OTC, create data, or invoke an API mutation.

`src/FE/config/release-environments.json` is deliberately fail-closed in the current `prelive` phase:

- production has `enableDevelopmentEndpoints: false` and `allowDestructivePlaywright: false`;
- staging has no URL and is not a runnable target;
- `playwright-release-runner.mjs` rejects every non-public scenario against production, regardless of phase.

The GitHub workflow exposes no target or scenario selector. It runs the one honest operation above, validates the Playwright source tests first, and uploads the short-lived public-smoke artifact. A workflow must not advertise a scenario that cannot complete in GitHub Actions.

## Critical suite: maintained, but intentionally blocked

The authenticated scenario code remains in the repository so it can be validated statically and completed once the proper environment exists. It is **not** current deployment evidence and is not configured for GitHub Actions.

The full suite stays blocked until both prerequisites are complete:

1. [WOR-309](https://linear.app/workslip/issue/WOR-309/isoleret-pre-merge-testmilj%C3%B8-og-automatisk-playwright-for-pr-sha) provides an isolated, candidate-SHA staging frontend, API, database, and synthetic data boundary.
2. [WOR-357](https://linear.app/workslip/issue/WOR-357/replace-playwright-dev-login-dependency-with-real-test-authentication) provides approved non-interactive test authentication without a dev-token endpoint, static token, or hidden login bypass.

Do not solve either prerequisite by running mutations against `https://app.mrsoftware.dk`, by adding `/api/dev/token` outside ASP.NET Development, or by putting mailbox credentials in the repository or CI logs.

Until those items are complete, authenticated work is reported as **implemented but Playwright-unvalidated**. That is an honest gap, not a passing critical test.

## Assignment duplication coverage

`assignment-lifecycle` now models the actual WOR-424 requirement rather than fabricating random users:

1. an Admin resolves the stable configured `User` and `Admin` identities in the active test organization;
2. the Admin creates a KLS task with **Opret en kopi af sagen til hver medarbejder** enabled;
3. the test proves there is exactly one independent copy per assignee;
4. each assignee completes and submits only their own copy;
5. the Admin approves both copies individually.

The harness never creates identities just to make a scenario pass. If the configured users are missing or do not have the expected roles, the scenario stops without exposing their addresses.

This is source-level coverage only until an isolated staging run succeeds.

## Authentication and evidence boundaries

Authenticated scenarios use the normal one-time-code UI and the four configured role identities described in [synthetic-test-identities.md](synthetic-test-identities.md). There is no approved automated inbox reader today, so a non-interactive authenticated run stops before `/api/auth/send-code`.

For a future allowed staging run, an operator may explicitly use a headed TTY session and enter received codes only in the browser. The harness caches an already-authenticated Admin token in memory for cleanup so it does not trigger an unnecessary additional OTC login.

Artifacts are minimized:

- public smoke may save a screenshot;
- authenticated scenarios save no screenshots while identity/customer redaction is not strong enough to make them safe;
- reports redact OTC values, bearer tokens, query secrets, and email addresses;
- traces are not uploaded;
- generated data uses a `PLAYWRIGHT` marker and cleanup failures are recorded without personal identifiers.

## Local Windows validation

Use `tools/playwright/run-critical-local.ps1` from the repository root. Today this is the supported direct run:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Direct `
  -Target Production `
  -Scenario public-smoke
```

The helper installs the version-matched Playwright runtime when required, validates release policy and source tests, resolves the committed target, and writes evidence under `artifacts/playwright-prod-smoke`.

If an operator's network requires an outbound HTTP(S) proxy, set the non-secret proxy origin in `WORKSLIP_PLAYWRIGHT_PROXY` for that shell. The runner accepts only a credential-free origin and never records it in the report. If that proxy performs TLS interception and its CA cannot be installed in Chromium, a local operator may additionally set `WORKSLIP_PLAYWRIGHT_IGNORE_HTTPS_ERRORS=true`; this is accepted only with the explicit proxy setting and does not change CI. Such a run proves browser flow/connectivity, not the target certificate chain.

`-Mode Workflow` intentionally supports only the same public production smoke. A critical scenario or staging target is rejected before Docker/`act` work begins.

## Completing the configuration later

After WOR-309 and WOR-357 are approved and deployed:

1. configure a separate HTTPS staging origin and isolated database with synthetic-only data;
2. update the committed release policy to `live`, keeping production flags false and enabling only the isolated staging target;
3. prove the candidate SHA is deployed to that target;
4. add a reviewed CI-safe authentication mechanism that uses the normal auth boundary and does not expose credentials or codes;
5. re-enable selected critical workflow inputs only after a successful staging run and artifact review;
6. keep production restricted to `public-smoke` permanently.

The frontend has no deployment-controlled dev-login switch. Local development buttons use `import.meta.env.DEV`, and `/api/dev/token` is mapped only in ASP.NET Development. Release policy must not claim otherwise.

## Source validation

`CI` validates the release-policy resolver, the release-runner guard, synthetic-auth fail-closed behavior, Playwright syntax, Postman JSON, documentation, frontend tests, and build. Those checks verify that the harness remains safe and internally coherent; they do not replace a successful browser run against the eventual isolated staging target.
