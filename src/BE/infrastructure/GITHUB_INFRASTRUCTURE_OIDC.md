# GitHub infrastructure OIDC

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

## Single idempotent deployment entry point

Operators run one command from `src/BE/infrastructure`:

```powershell
.\deploy.ps1 prod
```

`deploy.ps1` now performs the complete reconciliation in this order:

1. Microsoft Entra applications;
2. Azure infrastructure;
3. VAPID secret lifecycle;
4. GitHub infrastructure OIDC identity, federation, permissions and `AZURE_INFRA_CLIENT_ID`.

The separate bootstrap script remains an internal implementation component because the infrastructure identity must be created by an already-authorized Azure/Entra identity before GitHub can authenticate as that identity. Operators do not need a separate bootstrap flow.

## Idempotency

Repeated `deploy.ps1` runs converge on the same state:

- Bicep deployments use stable resource names and deterministic role-assignment names;
- existing managed identities and federated credentials are updated rather than duplicated;
- Microsoft Graph roles are checked before assignment;
- the GitHub environment variable is set to the current non-secret client ID;
- existing Entra, Azure, VAPID and infrastructure resources follow their established reconciliation logic.

A failed phase can be corrected and the same command rerun. No generated client secret, publish profile or `AZURE_CREDENTIALS` JSON is created.

## Prerequisites

- Azure CLI installed and authenticated to the intended tenant/subscription;
- GitHub CLI installed and authenticated to `github.com`;
- the Azure/Entra account has permission to deploy the existing infrastructure and assign the documented Azure and Microsoft Graph roles;
- the GitHub account can update environment variables for `rasm105k/Workslip-v2.0`.

`deploy.ps1` validates GitHub CLI authentication before making deployment changes.

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

The reconciliation resolves Graph role IDs from Microsoft Graph by role value instead of duplicating permission GUIDs.

## Normal GitHub deployment

After a successful local `deploy.ps1 prod` run and permission propagation:

1. Open **Actions**.
2. Select **Production infrastructure deploy**.
3. Select `main`.
4. Run the workflow.

The workflow remains bound to the protected `prod` environment, serialized, and restricted to `main`. It logs in through OIDC and runs `deploy-infrastructure.ps1 prod`.

## Verification

The setup is complete when:

- the managed identity and federated credential exist;
- the Azure role assignments above are present;
- the three Graph app-role assignments are present;
- GitHub environment `prod` contains `AZURE_INFRA_CLIENT_ID` as a variable;
- the manual workflow passes Azure login;
- infrastructure deployment completes;
- diagnostics configuration, Log Analytics reader access and API `/health` pass.

## Rollback

Disable the workflow or remove `AZURE_INFRA_CLIENT_ID` before removing permissions. Do not delete the identity while a deployment is running. Remove the federated credential, Graph assignments, Azure role assignments, custom role assignment and managed identity only after confirming no workflow depends on them.
