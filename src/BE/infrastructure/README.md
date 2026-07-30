# Infrastructure deployment

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** this directory, `main.bicep`, the deployment scripts and accepted infrastructure ADRs  
**Review cadence:** whenever Azure, Entra, SQL, GitHub OIDC, monitoring or secret handling changes  
**Linear:** WOR-190, WOR-212

Workslip has exactly three supported deployment entry points:

| Script | Purpose |
|---|---|
| `deploy.ps1` | Reconcile Entra and deploy Azure infrastructure. |
| `deploy-entra.ps1` | Reconcile only the Microsoft Entra application registrations and service principals. |
| `deploy-infrastructure.ps1` | Deploy only Azure resources using existing Entra state or read-only Entra discovery. |

Do not add another public deployment wrapper. Helper scripts are implementation details and must not be presented as operator entry points.

## Prerequisites

- Azure CLI authenticated to the intended subscription and tenant.
- An operator allowed to deploy the resource group, manage the SQL administrator group and read/write the production Key Vault and App Configuration resources.
- Microsoft `sqlcmd` on the machine running the infrastructure phase.
- Microsoft Graph application-management permission for the Entra phase.

Production defaults are `mrsoftware`, `prod` and `westeurope`. Pass explicit values for another environment.

## Full deployment

Run both phases through the primary entry point:

```powershell
.\deploy.ps1 prod
```

The sequence is:

1. `deploy-entra.ps1` reconciles the two Entra applications and service principals.
2. `deploy-infrastructure.ps1` deploys and reconciles Azure resources.

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

Vercel cache-purge credentials and project configuration are outside the Azure infrastructure deployment boundary. The infrastructure scripts neither require nor reconcile them.

## Internal helpers

`grant-web-api-sql-access.ps1` is called by `deploy-infrastructure.ps1`. It is an implementation helper, not a standalone operator command.

## Runtime SQL authentication

Production API connections use the user-assigned managed identity:

```text
Authentication=Active Directory Managed Identity;User Id=<managed-identity-client-id>
```

`Azure:Sql:ConnectionString` is a Key Vault reference. It contains no SQL username or password. The SQL administrator password remains a deployment-only bootstrap credential in Key Vault secret `Azure--Sql--AdminPassword` and is used only by the controlled `sqlcmd` provisioning step.

The identity currently receives `db_datareader`, `db_datawriter` and `db_ddladmin`. `db_ddladmin` is temporary while schema initialization still runs at API startup; remove it with WOR-136 when migrations move to deployment.

## Secret lifecycle

The infrastructure script owns these versionless Key Vault references:

| Configuration key | Key Vault secret | Behaviour |
|---|---|---|
| `Jwt:SigningKey` | `Jwt--SigningKey` | Generates a cryptographically random key when missing or when the legacy short deterministic value is detected. `WORKSLIP_JWT_SIGNING_KEY` is an explicit rotation override. |
| `Azure:Sql:ConnectionString` | `Azure--Sql--ConnectionString` | Reconciled to a passwordless managed-identity connection string after Bicep returns the identity client ID. |

Secrets are written through temporary files and cleared from script variables during cleanup. A JWT signing-key rotation invalidates outstanding local Workslip JWTs.

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

1. `Azure:Sql:ConnectionString` and `Jwt:SigningKey` are Key Vault references in App Configuration.
2. The SQL connection secret uses managed identity and contains no `Password=` or SQL user ID.
3. The API managed identity can connect and `/health` returns successfully after API deployment.
4. Microsoft login and one authenticated API request succeed.
5. The legacy OAuth credential display name is absent from the OAuth application after a full deployment.
6. In production, `Azure:Acs:SenderAddress` is `noreply@mrsoftware.dk` and the ACS domain verification states remain successful; non-production uses its Azure-managed sender.
7. The API Action Group contains the intended operations recipients and its test notification is received.
8. The availability test reports successful executions from all configured locations.
9. The API unavailable, HTTP 5xx and slow-response alert rules are enabled and reference the same Action Group.
10. GitHub environment `prod` still contains the current OIDC client, tenant and subscription IDs.

Production Azure execution, DNS changes, alert testing and secret rotation are explicit operator actions; repository changes alone do not prove they succeeded.
