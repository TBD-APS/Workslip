# GitHub infrastructure OIDC bootstrap

**Status:** Active  
**Owner:** Workslip repository owner  
**Linear:** WOR-315

The manual **Production infrastructure deploy** workflow uses a dedicated GitHub OIDC identity. It does not use a client secret, publish profile or the ordinary API package-deployment identity.

## Identity split

| Identity | Scope | Purpose |
|---|---|---|
| `id-mrsoftware-prod-github` | Production API App Service | Deploy an already-built API package. Its client ID remains in `AZURE_CLIENT_ID`. |
| `id-mrsoftware-prod-infra-github` | Reviewed infrastructure scopes | Reconcile Azure resources, role assignments, Key Vault/App Configuration data and the Graph resources used by infrastructure deployment. Its client ID is `AZURE_INFRA_CLIENT_ID`. |

Do not widen `id-mrsoftware-prod-github` to run infrastructure deployment.

## Why bootstrap is separate

The infrastructure identity cannot create its own identity, federation and permissions before it exists. A human Azure/Entra administrator therefore runs the bootstrap once. Normal later deployments run through GitHub OIDC.

The production resource group, Key Vault and App Configuration store must already exist. The bootstrap does not replace the initial administrator deployment of a new environment.

## One-time bootstrap

Prerequisites:

- Azure CLI installed and authenticated to the intended tenant/subscription;
- an account authorized to create managed identities, custom roles and role assignments at the documented scopes;
- an account authorized to grant Microsoft Graph application permissions;
- optional: GitHub CLI authenticated with permission to manage environment variables.

From `src/BE/infrastructure`:

```powershell
.\bootstrap-github-infrastructure-identity.ps1 prod -WhatIf
.\bootstrap-github-infrastructure-identity.ps1 prod -ConfigureGitHubEnvironment
```

`-WhatIf` performs Azure deployment planning only and makes no Azure, Graph or GitHub mutation.

Without `-ConfigureGitHubEnvironment`, the script prints the non-secret client ID. Add it in:

```text
GitHub repository
  Settings
    Environments
      prod
        Environment variables
          AZURE_INFRA_CLIENT_ID=<client-id>
```

`AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` remain protected GitHub environment secrets. `AZURE_INFRA_CLIENT_ID` is an identifier and is stored as an environment variable, not a secret.

## Granted permissions

Azure:

- `Contributor` on `rg-mrsoftware-prod`;
- `Role Based Access Control Administrator` on `rg-mrsoftware-prod`;
- `Key Vault Secrets Officer` on `kv-mrsoftware-prod`;
- `App Configuration Data Owner` on `appcs-mrsoftware-prod`;
- custom subscription role allowing only resource-provider read/registration.

Microsoft Graph application permissions:

- `Directory.Read.All`;
- `Group.ReadWrite.All`;
- `AppRoleAssignment.ReadWrite.All`.

The bootstrap resolves Graph role IDs from Microsoft Graph by role value instead of hardcoding permission GUIDs.

## Normal deployment

After bootstrap and RBAC propagation:

1. Open **Actions**.
2. Select **Production infrastructure deploy**.
3. Select `main`.
4. Run the workflow.

The workflow remains bound to the protected `prod` environment, serialized, and restricted to `main`. It logs in through OIDC and then runs `deploy-infrastructure.ps1 prod`.

## Verification

A bootstrap is not complete until:

- the managed identity and federated credential exist;
- the Azure role assignments above are present;
- the three Graph app-role assignments are present;
- GitHub environment `prod` contains `AZURE_INFRA_CLIENT_ID` as a variable;
- the manual workflow passes Azure login;
- infrastructure deployment completes;
- diagnostics configuration, Log Analytics reader access and API `/health` pass.

## Rollback

Disable the workflow or remove `AZURE_INFRA_CLIENT_ID` before removing permissions. Do not delete the identity while a deployment is running. Remove the federated credential, Graph assignments, Azure role assignments, custom role assignment and managed identity only after confirming no workflow depends on them.
