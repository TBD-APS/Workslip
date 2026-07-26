# Azure SQL identity and network runbook

- **Status:** Active
- **Owner:** Workslip maintainers
- **Source of truth:** `src/BE/infrastructure/main.bicep`, `deploy.ps1` and `Test-AzureSqlSecurity.ps1`
- **Review cadence:** Before go-live and on identity, App Service plan, SQL or deployment changes

This runbook moves production to passwordless Azure SQL access and verifies the actual runtime and network path. The first rehearsal must use a disposable environment. Production execution requires an approved change window.

## Access model

| Principal | Purpose | SQL/database access | Azure control-plane access |
|---|---|---|---|
| `id-<company>-<environment>` | API runtime | Contained user: reader, writer and temporary DDL admin | App Configuration, Key Vault, storage, ACS and required Graph application roles |
| `id-<company>-<environment>-deploy` | Deployment | Member of the SQL Entra admin group | GitHub OIDC, Website Contributor on the API and SQL Security Manager on the logical server |
| Named administrator | Break-glass and manual infrastructure deployment | Member of the SQL Entra admin group | Rights required by `deploy.ps1` |

The runtime identity must not be a member of the SQL administrator group and must not retain GitHub federation or deployment RBAC after the transition is finalized.

## Prerequisites

- PowerShell 7, Azure CLI with Bicep and `sqlcmd` 18.
- An Azure administrator who can deploy the template and manage the Entra group/app registrations it defines.
- GitHub environment administrator access for the target environment.
- A recorded Azure SQL restore point/point-in-time recovery check.

Do not delete the old Key Vault SQL secrets during the first rehearsal. They become unused when Entra-only authentication is enabled and can be removed separately after validation.

## Rehearsal and rollout

1. Build and review the template:

   ```powershell
   az bicep build --file src/BE/infrastructure/main.bicep
   ```

2. Deploy the identity, Entra-only authentication, database user and exact App Service IP allowlist:

   ```powershell
   ./src/BE/infrastructure/deploy.ps1 -Environment prod
   ```

   The script runs two incremental deployments. Between them it adds the deployment identity to the SQL Entra administrator group. The second deployment creates/repairs the contained runtime database user. Only after that succeeds does the script remove the runtime identity from the SQL administrator group.

3. Copy the emitted `DEPLOYMENT_IDENTITY_CLIENT_ID` value into the GitHub `prod` environment secret `AZURE_CLIENT_ID`. `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` remain unchanged.

4. Manually run `.github/workflows/main_api-npteknik-prod.yml`. It deploys the API and runs the security validation.

5. Run the validation independently and retain the output with the change record:

   ```powershell
   ./src/BE/infrastructure/Test-AzureSqlSecurity.ps1 `
     -Environment prod `
     -CompanyName npteknik
   ```

   It must prove all of the following:

   - Microsoft Entra-only authentication is enabled.
   - TLS 1.2 is the SQL server minimum.
   - SQL firewall rules exactly match the API's possible outbound IPs.
   - `GET /health/ready` reaches SQL through the runtime identity.
   - an authenticated external deployment runner receives Azure SQL firewall error `40615`.

6. After the GitHub workflow succeeds with the deployment identity, remove legacy deployment access from the runtime identity:

   ```powershell
   ./src/BE/infrastructure/deploy.ps1 `
     -Environment prod `
     -FinalizeDeploymentIdentitySeparation
   ```

7. Run step 5 again. Record the workflow URL, deployment names, validation time and operator.

## Expected production state

```powershell
az sql server ad-only-auth get `
  --resource-group rg-npteknik-prod `
  --name db-npteknik-prod-server

az sql server firewall-rule list `
  --resource-group rg-npteknik-prod `
  --server db-npteknik-prod-server `
  --output table
```

`azureADOnlyAuthentication` must be `true`. Every firewall rule must be named `AllowWebApiOutbound<number>`, use one identical start/end IP and correspond to `possibleOutboundIpAddresses` on `api-npteknik-prod`.

## Failure and recovery

- If the first infrastructure deployment fails, do not run finalization. Correct the deployment error and rerun; the scripts are designed to reconcile the same named resources.
- If `/health/ready` fails, compare the runtime identity client ID in App Configuration with the identity assigned to the Web App and verify that the contained database user exists.
- If the firewall sets differ, rerun the infrastructure deployment. Do not restore `AllowAzureServices`.
- If GitHub OIDC fails after changing `AZURE_CLIENT_ID`, restore the previous secret temporarily, confirm the new federated credential subject, then retry. Do not add GitHub federation back to the runtime identity as the permanent fix.
- Do not re-enable SQL password authentication as an automatic rollback. Use the named Entra administrator to forward-fix identity, role or firewall configuration.

## Follow-up

- [WOR-162](https://linear.app/workslip/issue/WOR-162/etabler-ef-core-migrations-og-genereret-database-deploy-for-go-live): move schema changes out of API startup, then remove `db_ddladmin` from the runtime identity.
- After two successful rehearsals and approval, delete the obsolete SQL connection/admin password secrets from Key Vault.
- When the App Service plan moves to Basic or higher, implement Private Link and disable SQL public network access.
