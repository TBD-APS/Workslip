# Infrastructure deployment

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** this directory, `main.bicep`, the deployment scripts and accepted infrastructure ADRs  
**Review cadence:** whenever Azure, Entra, SQL, GitHub OIDC, monitoring or secret handling changes  
**Linear:** WOR-190, WOR-212, WOR-223

Workslip has exactly three supported deployment entry points:

| Script | Purpose |
|---|---|
| `deploy.ps1` | Reconcile Entra, deploy Azure infrastructure and reconcile deployment-owned runtime secrets. |
| `deploy-entra.ps1` | Reconcile only the Microsoft Entra application registrations and service principals. |
| `deploy-infrastructure.ps1` | Deploy only Azure resources using existing Entra state or read-only Entra discovery. |

Do not add another public deployment wrapper. Helper scripts such as `reconcile-vapid-secret.ps1` are implementation details and must not be presented as operator entry points.

## Prerequisites

- Azure CLI authenticated to the intended subscription and tenant.
- An operator allowed to deploy the resource group, manage the SQL administrator group and read/write the production Key Vault and App Configuration resources.
- Microsoft `sqlcmd` on the machine running the infrastructure phase.
- Microsoft Graph application-management permission for the Entra phase.

Production defaults are `mrsoftware`, `prod` and `westeurope`. Pass explicit values for another environment.

## Full deployment

Run all supported phases through the primary entry point:

```powershell
.\deploy.ps1 prod
```

The sequence is:

1. `deploy-entra.ps1` reconciles the two Entra applications and service principals.
2. `deploy-infrastructure.ps1` deploys and reconciles Azure resources.
3. `reconcile-vapid-secret.ps1` preserves or creates the secure VAPID private key, creates its App Configuration Key Vault reference and restarts the API.

The VAPID phase never prints key material. It preserves an enabled `Vapid--PrivateKey` secret and generates one valid P-256 private scalar only when the secret is missing or disabled.

## Entra only

Run this phase when creating an environment or changing application-registration settings:

```powershell
.\deploy-entra.ps1 prod
```

It reconciles the stable alternate keys:

- `workslip-oauth-server-{environment}`
- `workslip-client-{environment}`

Resolved object and client IDs are written to the ignored local state file:

```text
entra.{environment}.local.json
```

The script preserves existing managed role/scope IDs and does not create an OAuth client secret. The browser authenticates with authorization code + PKCE; the API validates bearer tokens.

## Azure infrastructure only

Run Azure resource deployment without modifying Entra registrations:

```powershell
.\deploy-infrastructure.ps1 prod
```

The script uses the local Entra state when present. Otherwise, it performs read-only discovery from Azure App Configuration and the stable Graph alternate keys. It fails with an instruction to run `deploy-entra.ps1` when no valid pair can be resolved.

The infrastructure phase:

1. validates the environment and tenant;
2. writes a temporary compile-time Entra handoff;
3. deploys `main.bicep` once;
4. reconciles Azure-owned deployment secrets without exposing them on command lines;
5. provisions the API user-assigned managed identity in Azure SQL;
6. restores the committed handoff placeholder.

An infrastructure-only deployment does not generate the VAPID private key. Use the full `deploy.ps1` entry point when establishing a new environment or repairing a missing VAPID secret.

Vercel cache-purge credentials and project configuration are outside the Azure infrastructure deployment boundary. The infrastructure scripts neither require nor reconcile them.

## Internal helpers

`grant-web-api-sql-access.ps1` and `reconcile-vapid-secret.ps1` are called by supported deployment entry points. They are implementation helpers, not standalone operator commands.

The SQL helper temporarily allows the deployment machine's public IPv4 address while running `sqlcmd`, then deletes the rule through `az sql server firewall-rule delete`. Azure SQL's delete command does not accept the `--yes` option; do not copy confirmation flags from MySQL or App Configuration CLI commands into this cleanup path.

The VAPID helper owns private-key generation, Key Vault storage, the `Vapid:PrivateKey` Key Vault reference and API restart.

## Runtime SQL authentication

Production API connections use the user-assigned managed identity:

```text
Authentication=Active Directory Managed Identity;User Id=<managed-identity-client-id>
```

`Azure:Sql:ConnectionString` is a Key Vault reference. It contains no SQL username or password. The SQL administrator password remains a deployment-only bootstrap credential in Key Vault secret `Azure--Sql--AdminPassword` and is used only by controlled provisioning steps.

The ordinary API runtime identity has normal data read/write access and must **not** be a member of `db_ddladmin`. Production schema/data migrations are executed before API deployment by the dedicated `id-<company>-<environment>-migration` identity, which receives `db_ddladmin`, `db_datareader` and `db_datawriter` plus the narrow Azure SQL firewall-management permission required for the ephemeral runner connection. ADR 0006 and `database/migrations/README.md` own this boundary.

## Secret lifecycle

The full deployment owns these versionless Key Vault references:

| Configuration key | Key Vault secret | Behaviour |
|---|---|---|
| `Jwt:SigningKey` | `Jwt--SigningKey` | Generates a cryptographically random key when missing or when the legacy short deterministic value is detected. `WORKSLIP_JWT_SIGNING_KEY` is an explicit rotation override. |
| `Azure:Sql:ConnectionString` | `Azure--Sql--ConnectionString` | Reconciled to a passwordless managed-identity connection string after Bicep returns the identity client ID. |
| `Vapid:PrivateKey` | `Vapid--PrivateKey` | Preserves an enabled secret and generates one valid P-256 private scalar when the secret is missing or disabled. |

Secrets are written through temporary files and cleared from script variables during cleanup. A newly generated VAPID key invalidates old browser subscriptions until each installed PWA completes its authenticated subscription-repair flow.

## Microsoft Graph permissions

`main.bicep` is the single source of truth for API runtime Graph app-role assignments:

- `User.ReadWrite.All`
- `User.Invite.All`
- `Application.Read.All`
- `AppRoleAssignment.ReadWrite.All`

These permissions support external-user lookup/invitation/deletion, API service-principal lookup and app-role assignment. Deployment scripts must not duplicate this assignment set.

## ACS custom sender

Production selects the verified `mrsoftware.dk` ACS email domain and `noreply@mrsoftware.dk` sender. Non-production environments use their Azure-managed domain and generated `DoNotReply@<domain>.azurecomm.net` sender. There is no operator activation parameter; the environment determines the sender.

The Azure-managed domain remains linked in production as an emergency rollback resource. Non-production deployments do not provision or link the production custom domain.

DNS verification must remain valid for Domain, SPF, DKIM and DKIM2. See `../../../Docs/acs-email-setup.md` for maintenance and smoke-test procedures.

## Azure Monitor API alerts

`monitoring.bicep` provisions one Azure Monitor Action Group and three stateful API alert rules:

| Alert | Condition | Severity |
|---|---|---|
| API unavailable | The public `/health` endpoint fails from at least three of five Azure test locations during a five-minute window. | Critical (0) |
| HTTP 5xx | The App Service emits one or more HTTP 5xx responses during a five-minute window. | Error (1) |
| Slow API | Average App Service response time exceeds five seconds during a five-minute window. | Warning (2) |

The availability test runs every five minutes from five regions, has retries enabled and validates HTTP 200, TLS validity and certificate lifetime. Standard availability tests are billable Azure Monitor executions; review Azure pricing before deploying additional environments or locations.

Alert recipients are maintained in `monitoring.config.json`. This is intentionally deployment-time operations configuration rather than a query against the Workslip database: alerts must still be deliverable when the API or SQL database is unavailable. Keep the list aligned with the people expected to respond to production incidents. Do not place credentials or notification-service secrets in this file.

After deployment, use Azure Monitor's **Test action group** function to verify delivery. Do not deliberately stop production or generate production errors solely to test an alert. Tune the response-time threshold if the F1 App Service cold-start behaviour creates repeated non-actionable notifications.

## Required post-deployment verification

A successful script exit is not sufficient release evidence. Verify:

1. `Azure:Sql:ConnectionString`, `Jwt:SigningKey` and `Vapid:PrivateKey` are versionless Key Vault references in App Configuration.
2. Key Vault contains an enabled `Vapid--PrivateKey` secret.
3. The SQL connection secret uses managed identity and contains no `Password=` or SQL user ID.
4. The API managed identity can connect and `/health` returns successfully after API deployment.
5. Microsoft login and one authenticated API request succeed.
6. Authenticated `GET /api/push-subscriptions/public-key` returns `200` without exposing private material.
7. Open or re-authenticate one installed PWA so it registers or repairs its subscription, then background the app and verify one OS-level notification.
8. The legacy OAuth credential display name is absent from the OAuth application after a full deployment.
9. In production, `Azure:Acs:SenderAddress` is `noreply@mrsoftware.dk` and the ACS domain verification states remain successful; non-production uses its Azure-managed sender.
10. The temporary SQL firewall rule `AllowSqlProvisioningScript` is absent after deployment.
11. The API Action Group contains the intended operations recipients and its test notification is received.
12. The availability test reports successful executions from all configured locations.
13. The API unavailable, HTTP 5xx and slow-response alert rules are enabled and reference the same Action Group.
14. GitHub environment `prod` still contains the current OIDC client, tenant and subscription IDs.

Production Azure execution, DNS changes, alert testing and secret rotation are explicit operator actions; repository changes alone do not prove they succeeded.
