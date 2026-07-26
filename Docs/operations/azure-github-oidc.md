# Azure GitHub OIDC deployment

Status: Active  
Owner: Workslip maintainers  
Source of truth: Azure workflow and infrastructure templates  
Review cadence: after GitHub or Azure identity changes

## Contract

Production deploys use the protected GitHub environment `prod` and a dedicated Azure managed identity with `Website Contributor` limited to the API App Service.

GitHub currently issues this immutable OIDC subject:

```text
repo:rasm105k@31623093/Workslip-v2.0@1245555609:environment:prod
```

The owner and repository IDs are part of the security contract. Do not replace this with the legacy name-only subject.

## Fresh deployment

Run:

```powershell
src/BE/infrastructure/deploy-with-github-oidc.ps1
```

The wrapper runs the existing `deploy.ps1` infrastructure deployment and then deploys `github-oidc-immutable.bicep`, which creates the federated credential matching GitHub's actual token subject.

After deployment:

1. Copy the printed `GITHUB_DEPLOYMENT_CLIENT_ID` from the base deployment.
2. Set `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` in the GitHub environment `prod`.
3. Run `.github/workflows/main_api-npteknik-prod.yml` manually.
4. Verify that Azure login and App Service deployment both succeed.

The legacy name-only credential may remain temporarily alongside the immutable credential. It cannot authenticate because GitHub no longer presents that subject.

## Validation

Run:

```text
python tools/ci/check_azure_oidc_contract.py
```

The check rejects stale `github.event.inputs` deployment expressions and verifies that the workflow, immutable credential template, and this runbook remain aligned.
