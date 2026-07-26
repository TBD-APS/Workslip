GitHub workflow: `.github/workflows/integration-tests.yml`.

## OpenAPI and Scalar

The host registers ASP.NET Core OpenAPI and Scalar. These surfaces must be treated as non-production developer/integration tooling unless production exposure is explicitly approved and protected. The maintained contract guide lives in `../../../Docs/api/README.md`.

## Deployment

The API deployment workflow is `.github/workflows/main_api-npteknik-prod.yml`. It builds and publishes the .NET project, authenticates to Azure through OIDC and deploys to an Azure Web App.

Production uses the protected GitHub environment `prod`. The Azure infrastructure creates a dedicated GitHub deployment managed identity with `Website Contributor` limited to the API App Service. GitHub currently issues the immutable OIDC subject:

```text
repo:rasm105k@31623093/Workslip-v2.0@1245555609:environment:prod
```

The owner and repository IDs are part of the security contract. Do not replace this with the legacy name-only subject.

After deploying the infrastructure, configure these environment secrets in the GitHub `prod` environment:

- `AZURE_CLIENT_ID`: deployment output `GITHUB_DEPLOYMENT_CLIENT_ID`
- `AZURE_TENANT_ID`: Microsoft Entra tenant ID
- `AZURE_SUBSCRIPTION_ID`: target Azure subscription ID

They are identifiers, not passwords. Do not add an Azure client secret, `AZURE_CREDENTIALS` JSON or an App Service publish profile.

After recreating the Azure resource group:

1. Run `src/BE/infrastructure/deploy-with-github-oidc.ps1`. This runs the base deployment and then creates the immutable GitHub federated credential.
2. Copy the printed `GITHUB_DEPLOYMENT_CLIENT_ID` from the base deployment.
3. Set the three `prod` environment secrets above and configure the required reviewer.
4. Run the API workflow manually once to verify OIDC login and App Service deployment.

The legacy name-only federated credential may remain alongside the immutable credential until the main infrastructure template can be simplified safely. It does not grant access because GitHub no longer presents that subject.

Run `python tools/ci/check_azure_oidc_contract.py` after changing the Azure workflow, OIDC module or this deployment documentation.

Workflow definitions are evidence of intended automation, not evidence that a deployment or rollback has succeeded. Record actual release validation separately.
