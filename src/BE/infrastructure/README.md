# Infrastructure deployment

**Status:** Active  
**Owner:** Workslip repository owner  
**Topology source of truth:** `TBD-APS/mr-saassy/infrastructure/workloads/workslip/azure`<br>
**Review cadence:** whenever Azure, Entra, SQL, GitHub OIDC, monitoring or secret handling changes  
**Linear:** WOR-190, WOR-212, WOR-223

The Bicep and deployment scripts in this directory are compatibility material
while production topology ownership is centralized in MR SAAS’y. Keep them
aligned with that canonical baseline while product workflows still reference
them.

Workslip has exactly three supported deployment entry points, plus one read-only entry point that previews them:

| Script | Purpose |
|---|---|
| `deploy.ps1` | Reconcile Entra, deploy Azure infrastructure and reconcile deployment-owned runtime secrets. |
| `deploy-entra.ps1` | Reconcile only the Microsoft Entra application registrations and service principals. |
| `deploy-infrastructure.ps1` | Deploy only Azure resources using existing Entra state or read-only Entra discovery. |
| `plan.ps1` | Preview all four phases. Changes nothing, and has no parameter that lets it. |

Do not add another public deployment *wrapper*. `plan.ps1` is admitted as the single exception because it removes a capability rather than adding one: it exists so previewing production cannot be turned into deploying production by a mistyped argument. Any further entry point must deploy nothing that an existing one already deploys.

Helper scripts such as `reconcile-vapid-secret.ps1` are implementation details and must not be presented as operator entry points.

## Previewing before deploying

Every phase accepts `-WhatIf` and reports what it would do without doing it.

```powershell
./plan.ps1 prod          # preferred: cannot mutate
./deploy.ps1 prod -WhatIf   # same preview, from the deploying entry point
```

Per phase:

| Phase | Preview behaviour |
|---|---|
| Entra | Lists the Microsoft Graph upserts it would send. Graph has no server-side what-if, so this is the intended writes, not a computed diff. |
| Azure infrastructure | Runs `az deployment group what-if` and prints the resource diff. |
| VAPID secret | Reports whether the private key would be created. |
| GitHub OIDC identity | Runs a subscription-level what-if and skips the GitHub environment write. |

A preview needs neither `sqlcmd` nor the GitHub CLI, because it never reaches the steps that use them.

Two limits worth knowing before trusting the output:

- The infrastructure phase previews against the Entra registrations that exist **now**. In a tenant where the Entra phase has not run yet, the registrations are absent, and both phases stop early and say so. Run the Entra phase for real first, then preview again to get a meaningful infrastructure diff.
- `what-if` compares against deployed resource state. It does not predict what the deployment scripts do *after* ARM returns — Key Vault writes, App Configuration references, SQL principals and SQL admin group membership are all outside its view. The preview lists those as skipped rather than pretending to diff them.

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
2. deploys `main.bicep` once, passing the resolved Entra identifiers and alert recipients as parameters;
3. reconciles Azure-owned deployment secrets without exposing them on command lines;
4. provisions the API user-assigned managed identity in Azure SQL.

The `live` production environment uses an S1 Standard App Service worker with
`alwaysOn` enabled and a `staging` deployment slot. This is the minimum live
capacity because Kudu needs space for release artifacts and the deployment
workflow validates a candidate before traffic is swapped. The legacy `prod`
boundary and other environments remain on F1 with `alwaysOn` disabled. The
compatibility script reconciles an existing live plan to S1 rather than leaving
it on an unsupported tier.

The template takes no compile-time file input. Everything instance-specific arrives as a deployment parameter, so `main.bicep` describes *a* Workslip environment rather than this one, and a deployment no longer writes to the working tree as a side effect. `monitoring.config.json` remains operator configuration; the deployment script reads it and passes the addresses through.

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

After deployment, use Azure Monitor's **Test action group** function to verify delivery. Do not deliberately stop production or generate production errors solely to test an alert. Tune the response-time threshold if App Service startup behaviour creates repeated non-actionable notifications.

## Cost budget

`budgets.bicep` provisions one monthly `Microsoft.Consumption` budget scoped to the resource group. It reuses the action group from `monitoring.bicep`, so cost warnings reach the same mailboxes as health alerts and there is no second recipient list to maintain.

| Notification | Fires when |
|---|---|
| Actual 50% | Half the monthly budget is already spent |
| Actual 80% | Spend is approaching the ceiling |
| Actual 100% | The budget is exceeded |
| Forecasted 100% | Azure projects the month to end over budget |

The forecasted threshold is the one worth acting on — it warns before the money is gone. A fresh subscription has no history to project from, so expect it to stay quiet through the first billing period.

The budget alarms; it does not cap. Azure keeps serving traffic after the threshold is passed.

| Parameter | Default | Notes |
|---|---|---|
| `-BudgetMonthlyAmount` | `800` | **In the billing currency of the subscription, which is not necessarily DKK.** Confirm the currency before trusting the number. The default leaves headroom over the ~534/month lean production baseline so it alarms on a runaway, not on normal operation. |
| `-BudgetEnabled` | `$true` | Set `$false` only if the deploying identity cannot write `Microsoft.Consumption` budgets. Cost alerting is then absent — record why. |

`Microsoft.Consumption` is registered by `deploy-infrastructure.ps1` alongside the other providers. Without it the first deployment into a fresh subscription fails on an unregistered provider.

Set the amount from measured consumption once the environment has run for a full billing period, not from an estimate. Related: the standard availability test in `monitoring.bicep` runs from five locations every five minutes and is itself a meaningful line item — measure it before adding environments or locations.

## Platform observability

`observability.bicep` covers the resources the API depends on but that the API alerts cannot see. When SQL saturates or blob storage degrades, the API itself often stays up and healthy, so `apiAvailabilityAlert` and `apiHttp5xxAlert` stay quiet while users experience failures.

### Alerts

| Alert | Condition | Severity |
|---|---|---|
| SQL DTU saturation | Average `dtu_consumption_percent` above 80% over fifteen minutes | Warning (2) |
| SQL storage saturation | Average `storage_percent` above 80% over one hour | Warning (2) |
| Storage availability | Average `Availability` below 99% over five minutes | Error (1) |

The database is Basic tier — five DTU and a two gigabyte ceiling — so both limits are reachable under ordinary growth, not only under abuse. The DTU window is fifteen minutes rather than five because a single report render spikes the gauge on five DTU; a shorter window would alert on normal use. Database size moves slowly, so the storage window is an hour, which costs nothing in warning time and removes noise.

### Diagnostic streams

| Source | Collected | Not collected |
|---|---|---|
| SQL database | All logs, `Basic` metrics | — |
| Blob service | `StorageWrite`, `StorageDelete`, `Transaction` metrics | `StorageRead` |
| Communication Services | All logs, all metrics | — |

The workspace is capped at `dailyQuotaGb: 1`. That cap makes log selection more important, not less: a high-volume stream does not merely cost money, it crowds out the signals that matter once the cap is hit. Blob reads are by far the noisiest thing this system produces — every job image view is one — and carry almost no diagnostic value, so they are excluded while writes and deletes are kept.

Communication Services logs matter more than their volume suggests. Invitations and one-time codes go out through that resource, and when delivery stops nothing errors — the mail simply never arrives, and onboarding fails silently.

### Not yet covered

There is no alert on email delivery failure. The diagnostic stream above is the prerequisite, but the alert itself needs the metric or table names read off a live Communication Services resource; they could not be verified when this was written. Tracked separately.

### Dashboards

These streams exist to be reported on, not only alerted on. Everything above lands in the `logAnal-<company>-<env>` workspace and is queryable from day one, which is what a later dashboard will be built from — cost per tenant, email delivery rate, storage growth, database headroom. Keep that in mind before narrowing what is collected: an alert only needs the threshold, a dashboard needs the history behind it.

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
