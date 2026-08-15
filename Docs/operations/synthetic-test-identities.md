# Synthetic test identities

**Status:** Authentication boundary defined; authenticated execution is blocked pending isolated staging and approved test auth

**Owner:** Workslip product and release maintainers

**Source of truth:** approved environment configuration, deployed `/api/auth` behavior, and Playwright run evidence

**Review cadence:** Before any authenticated release-test run or identity strategy change

## Stable role identities

The authenticated Playwright code expects one configured identity for each role in the isolated test organization:

- `WORKSLIP_SYNTHETIC_USER_EMAIL`
- `WORKSLIP_SYNTHETIC_AUDITOR_EMAIL`
- `WORKSLIP_SYNTHETIC_ADMIN_EMAIL`
- `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL`

Addresses are personal/security configuration data. Keep them only in the approved environment or an operator's local process environment — never in repository files, commands, reports, screenshots, or logs.

The harness verifies each configured role through `/api/auth/me`. The configured assignment identities must also have distinct display names, because that is the accessible UI selector. It does not create, delete, or change ordinary `User`, `Auditor`, or `Admin` identities in order to make a scenario pass. In particular, the assignment-duplication scenario uses the existing `User` and `Admin` identities rather than inventing random employees that cannot actually log in.

### Rotatable Superadmin identity

`WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL` is also the source of truth for the explicit platform Superadmin bootstrap. It is deliberately a rotatable, non-permanent test identity rather than a personal break-glass account or a list of hardcoded permanent operators.

The explicit `bootstrap-superadmins` operation fails closed when this value is missing or malformed. It may create/reuse the configured Entra guest, assign the API `Superadmin` app role, reconcile the single Workslip platform-Superadmin row, and revoke the Superadmin app role from known legacy bootstrap identities during migration. The configured email itself must remain outside source control.

Changing `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL` is an identity rotation, not an ordinary user promotion. Run the explicit bootstrap after changing it so the old synthetic Superadmin loses its app-role assignment and the platform database converges to the new identity. Ordinary Admin or tenant users must never be promoted by this mechanism; if the configured email already belongs to a non-bootstrap Workslip user, bootstrap fails instead of moving or escalating that user.

Normal application startup does not run this bootstrap and normal login remains the supported OTC flow. Do not add a static Superadmin token, dev-login bypass, permanent personal email fallback, or frontend-only role override.

## Current fail-closed boundary

There is no approved automated inbox reader or CI authentication grant today. Consequently:

- `public-smoke` needs no identities and sends no mail;
- every authenticated non-interactive run stops before `/api/auth/send-code`;
- GitHub Actions runs only `public-smoke` and receives no synthetic email variables;
- there is no static application token, dev-token route, hidden browser state, or login bypass;
- production is never an allowed target for an authenticated or destructive flow.

This is intentional. Adding mailbox credentials, a provider API, a browser profile, another external processor, or a CI authentication grant requires a separate security/privacy review and an approved isolated target/authentication design tracked by the active release-test work.

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

This command is deliberately rejected while staging/auth automation is not configured. When it becomes available, enter each delivered code only in the visible browser; never paste it into the terminal. The harness requires both the exact `true` opt-in and a TTY.

## Artifact controls

Tokens and OTC values remain in process memory. The harness redacts bearer tokens, OTC paths, query secrets, and email addresses from JSON reports and failure summaries. Authenticated scenarios do not save screenshots or traces while the test environment is being completed.

Source tests prove this pre-send fail-closed behavior and redaction. They do not prove real email delivery, user provisioning, or a successful deployed OTC login. Until a permitted staging run succeeds, state that limitation explicitly as **implemented but Playwright-unvalidated**.
