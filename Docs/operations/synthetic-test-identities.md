# Synthetic test identities

**Status:** Authentication boundary defined; authenticated execution is blocked pending isolated staging and approved test auth

**Owner:** Workslip product and release maintainers

**Source of truth:** approved environment configuration, deployed `/api/auth` behavior, and Playwright run evidence

**Review cadence:** Before any authenticated release-test run or identity strategy change

## Stable role identities

The authenticated Playwright code expects one stable identity for each role in the isolated test organization:

- `WORKSLIP_SYNTHETIC_USER_EMAIL`
- `WORKSLIP_SYNTHETIC_AUDITOR_EMAIL`
- `WORKSLIP_SYNTHETIC_ADMIN_EMAIL`
- `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL`

Addresses are personal/security configuration data. Keep them only in the approved environment or an operator's local process environment — never in repository files, commands, reports, screenshots, or logs.

The harness verifies each configured role through `/api/auth/me`. The configured assignment identities must also have distinct display names, because that is the accessible UI selector. It does not create, delete, or change the four stable identities in order to make a scenario pass. In particular, the assignment-duplication scenario uses the existing `User` and `Admin` identities rather than inventing random employees that cannot actually log in.

## Current fail-closed boundary

There is no approved automated inbox reader or CI authentication grant today. Consequently:

- `public-smoke` needs no identities and sends no mail;
- every authenticated non-interactive run stops before `/api/auth/send-code`;
- GitHub Actions runs only `public-smoke` and receives no synthetic email variables;
- there is no static application token, dev-token route, hidden browser state, or login bypass;
- production is never an allowed target for an authenticated or destructive flow.

This is intentional. Adding mailbox credentials, a provider API, a browser profile, another external processor, or a CI authentication grant requires a separate security/privacy review and the isolated target tracked by WOR-309 and WOR-357.

## Future local interactive staging run

Once an isolated staging target is configured and explicitly allowed by the committed release policy, an operator with approved access to the required inboxes may opt into a headed TTY run:

```powershell
$env:WORKSLIP_PLAYWRIGHT_INTERACTIVE_OTC = 'true'
powershell -ExecutionPolicy Bypass -File .\tools\playwright\run-critical-local.ps1 `
  -Mode Direct `
  -Target Staging `
  -Scenario auth-session
Remove-Item Env:WORKSLIP_PLAYWRIGHT_INTERACTIVE_OTC
```

This command is deliberately rejected today because staging is not configured. When it becomes available, enter each delivered code only in the visible browser; never paste it into the terminal. The harness requires both the exact `true` opt-in and a TTY.

## Artifact controls

Tokens and OTC values remain in process memory. The harness redacts bearer tokens, OTC paths, query secrets, and email addresses from JSON reports and failure summaries. Authenticated scenarios do not save screenshots or traces while the test environment is being completed.

Source tests prove this pre-send fail-closed behavior and redaction. They do not prove real email delivery, user provisioning, or a successful deployed OTC login. Until a permitted staging run succeeds, state that limitation explicitly as **implemented but Playwright-unvalidated**.
