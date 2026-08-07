# Synthetic test identities

## Purpose

Authenticated Playwright release tests use dedicated synthetic Workslip users and the normal one-time-code authentication flow. The test suite must not use `/api/dev/token`, fake login buttons, embedded bearer tokens, or a human employee account.

The four baseline identities are:

| Role | Default mailbox |
|---|---|
| User | `user@<MAILOSAUR_SERVER_ID>.mailosaur.net` |
| Auditor | `auditor@<MAILOSAUR_SERVER_ID>.mailosaur.net` |
| Admin | `admin@<MAILOSAUR_SERVER_ID>.mailosaur.net` |
| Superadmin | `superadmin@<MAILOSAUR_SERVER_ID>.mailosaur.net` |

Each address can be overridden with `WORKSLIP_SYNTHETIC_<ROLE>_EMAIL`. Overrides must still belong to the configured Mailosaur server.

## Authentication flow

`playwright-prod-smoke.mjs` drives the real deployed UI:

1. Open `/login`.
2. Select the one-time-code login.
3. Submit the synthetic email through `/api/auth/send-code`.
4. The test runner polls Mailosaur for the newly received message.
5. Enter the six-digit code in the deployed Workslip UI.
6. Workslip verifies the code through `/api/auth/verify-code/{code}` and returns the normal short-lived application JWT.

The Mailosaur API key exists only in the GitHub runner process. It is not passed to browser JavaScript, written to reports, or retained in Playwright artifacts.

## GitHub configuration

Required:

- GitHub Secret `MAILOSAUR_API_KEY`.
- GitHub Variable `MAILOSAUR_SERVER_ID`.

Optional GitHub Variables:

- `WORKSLIP_SYNTHETIC_USER_EMAIL`
- `WORKSLIP_SYNTHETIC_AUDITOR_EMAIL`
- `WORKSLIP_SYNTHETIC_ADMIN_EMAIL`
- `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL`

`public-smoke` does not require these values because it performs no authenticated work.

## One-time provisioning

The four Workslip users must exist in the release-test tenant before authenticated scenarios run. Use `src/FE/scripts/bootstrap-synthetic-test-identities.mjs` with a short-lived administrator token obtained through a normal Workslip login.

Required environment variables for the bootstrap:

- `WORKSLIP_API_URL`
- `WORKSLIP_BOOTSTRAP_ADMIN_TOKEN`
- `MAILOSAUR_SERVER_ID`

The script reads the tenant user list, leaves correctly configured identities unchanged, fails on role mismatches, and creates only missing identities through the normal `/api/users` contract.

For a production-looking API target the script fails closed unless `WORKSLIP_ALLOW_PRODUCTION_SYNTHETIC_BOOTSTRAP=true` is explicitly supplied. That override is only for the deliberate one-time setup of a release-test tenant. Do not store `WORKSLIP_BOOTSTRAP_ADMIN_TOKEN` as a permanent CI secret; remove it after provisioning.

Example PowerShell session:

```powershell
$env:WORKSLIP_API_URL = "https://<api-host>"
$env:WORKSLIP_BOOTSTRAP_ADMIN_TOKEN = "<short-lived-token>"
$env:MAILOSAUR_SERVER_ID = "<server-id>"
node .\src\FE\scripts\bootstrap-synthetic-test-identities.mjs
Remove-Item Env:WORKSLIP_BOOTSTRAP_ADMIN_TOKEN
```

## Data and security rules

Synthetic users must live only in the designated release-test tenant. Test-generated customers, jobs, users, and worksheets follow the existing Playwright cleanup policy. The baseline synthetic identities are durable test principals and are not deleted by scenario cleanup.

No replacement auth bypass is allowed. If Mailosaur or OTC authentication is unavailable, authenticated tests fail rather than falling back to a static token or privileged endpoint.
