# Synthetic test identities

## Purpose

Authenticated Playwright release tests use dedicated synthetic Workslip users and the normal one-time-code authentication flow. The test suite must not use `/api/dev/token`, fake login buttons, embedded bearer tokens, a human employee mailbox, or a temporary test-mail vendor account.

Email delivery is verified through an organization-owned Exchange Online shared mailbox. GitHub Actions reads that mailbox through Microsoft Graph using GitHub OIDC -> Microsoft Entra workload identity federation. No long-lived Graph client secret is required.

## Mailbox model

Create one dedicated shared mailbox in the Microsoft 365 tenant, for example `workslip-e2e@<company-domain>`.

Configure four durable role addresses that all deliver to that mailbox:

- `WORKSLIP_SYNTHETIC_USER_EMAIL`
- `WORKSLIP_SYNTHETIC_AUDITOR_EMAIL`
- `WORKSLIP_SYNTHETIC_ADMIN_EMAIL`
- `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL`

They can be normal aliases on the shared mailbox. The Admin address must support Exchange Online plus-addressing because the tenant-isolation scenario derives unique secondary-admin addresses from it, for example `admin+<run-tag>@<company-domain>`. Exchange Online enables plus-addressing by default.

The shared mailbox should be company/test infrastructure. Do not bind CI to a developer's personal inbox. If another Microsoft 365 tenant is used, create the shared mailbox there and treat that tenant as an explicit external test-infrastructure dependency.

## Authentication flow

`playwright-prod-smoke.mjs` drives the real deployed UI:

1. Open `/login`.
2. Select the one-time-code login.
3. Submit the synthetic email through `/api/auth/send-code`.
4. The test runner obtains a GitHub OIDC assertion and exchanges it with Microsoft Entra for a Microsoft Graph access token.
5. The runner polls the dedicated shared mailbox and matches the original recipient using message recipients/transport headers.
6. Enter the six-digit code in the deployed Workslip UI.
7. Workslip verifies the code through `/api/auth/verify-code/{code}` and returns the normal short-lived application JWT.

The Graph access token exists only in the runner process. It is not passed to browser JavaScript, written to reports, or retained in Playwright artifacts.

## Microsoft 365 / Entra setup

Use a dedicated Entra application/service principal for the Playwright mailbox reader.

Configure a federated identity credential for the trusted GitHub workflow/ref or GitHub Environment used by the release test. The workflow needs `id-token: write` only to request the short-lived GitHub OIDC assertion.

Grant mailbox read access with Exchange Online **RBAC for Applications**, scoped only to the synthetic shared mailbox. Use the `Application Mail.Read` role and a management scope that includes only the test mailbox. Do not also grant an unscoped Microsoft Graph `Mail.Read` application permission in Entra; unscoped Entra grants are additive and would defeat the Exchange mailbox scope.

The service principal requires no mail-send permission and no write permission.

## GitHub configuration

Required repository/environment variables:

- `WORKSLIP_GRAPH_TENANT_ID`
- `WORKSLIP_GRAPH_CLIENT_ID`
- `WORKSLIP_SYNTHETIC_MAILBOX`
- `WORKSLIP_SYNTHETIC_USER_EMAIL`
- `WORKSLIP_SYNTHETIC_AUDITOR_EMAIL`
- `WORKSLIP_SYNTHETIC_ADMIN_EMAIL`
- `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL`

No permanent mailbox/API secret is required by the GitHub workflow.

`public-smoke` does not consume the mailbox because it performs no authenticated work.

For a manual local run, `WORKSLIP_GRAPH_ACCESS_TOKEN` may be supplied as a short-lived Graph token instead of GitHub OIDC. Never store that token in source control or as a durable CI secret.

## One-time Workslip provisioning

The four Workslip users must exist in the release-test tenant before authenticated scenarios run. Use `src/FE/scripts/bootstrap-synthetic-test-identities.mjs` with a short-lived **Superadmin** token obtained through a normal Workslip login.

Required environment variables for the bootstrap:

- `WORKSLIP_API_URL`
- `WORKSLIP_BOOTSTRAP_SUPERADMIN_TOKEN`
- `WORKSLIP_SYNTHETIC_USER_EMAIL`
- `WORKSLIP_SYNTHETIC_AUDITOR_EMAIL`
- `WORKSLIP_SYNTHETIC_ADMIN_EMAIL`
- `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL`

The script first verifies `/api/auth/me` reports `Superadmin`, then reads the tenant user list, leaves correctly configured identities unchanged, fails on role mismatches, and creates only missing identities through the normal `/api/users` contract.

For a production-looking API target the script fails closed unless `WORKSLIP_ALLOW_PRODUCTION_SYNTHETIC_BOOTSTRAP=true` is explicitly supplied. That override is only for the deliberate one-time setup of a release-test tenant. Do not store `WORKSLIP_BOOTSTRAP_SUPERADMIN_TOKEN` as a permanent CI secret; remove it after provisioning.

Example PowerShell session:

```powershell
$env:WORKSLIP_API_URL = "https://<api-host>"
$env:WORKSLIP_BOOTSTRAP_SUPERADMIN_TOKEN = "<short-lived-token>"
$env:WORKSLIP_SYNTHETIC_USER_EMAIL = "<user-test-address>"
$env:WORKSLIP_SYNTHETIC_AUDITOR_EMAIL = "<auditor-test-address>"
$env:WORKSLIP_SYNTHETIC_ADMIN_EMAIL = "<admin-test-address>"
$env:WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL = "<superadmin-test-address>"
node .\src\FE\scripts\bootstrap-synthetic-test-identities.mjs
Remove-Item Env:WORKSLIP_BOOTSTRAP_SUPERADMIN_TOKEN
```

## Data and security rules

Synthetic users must live only in the designated release-test tenant. Test-generated customers, jobs, users, and worksheets follow the existing Playwright cleanup policy. The baseline synthetic identities are durable test principals and are not deleted by scenario cleanup.

The mailbox-reader application must remain read-only and mailbox-scoped. The shared mailbox account itself should have interactive sign-in blocked where supported by the tenant configuration.

No replacement auth bypass is allowed. If Exchange Online, Microsoft Graph, GitHub OIDC, or OTC authentication is unavailable, authenticated tests fail rather than falling back to a static token or privileged endpoint.
