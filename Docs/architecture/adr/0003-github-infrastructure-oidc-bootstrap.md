# ADR 0003: Separate GitHub OIDC identity for infrastructure deployment

**Status:** Accepted  
**Date:** 2026-08-02  
**Owner:** Workslip architecture owner  
**Linear:** WOR-315

## Context

Workslip already has a narrow GitHub OIDC identity for deploying an API package to the production App Service. Full infrastructure reconciliation needs materially broader Azure control-plane, RBAC, Key Vault, App Configuration and Microsoft Graph permissions.

The first manual infrastructure workflow incorrectly expected `AZURE_INFRA_CLIENT_ID` as a GitHub secret even though a client ID is not credential material. More importantly, no separately authorized infrastructure identity had been provisioned, so the workflow could not authenticate.

Using the ordinary API deployment identity would require widening an App Service-scoped identity into an infrastructure administrator. The infrastructure identity also cannot create its own initial authentication trust before it exists.

## Decision

1. Keep `id-mrsoftware-prod-github` restricted to API package deployment at the App Service scope.
2. Provision a separate `id-mrsoftware-prod-infra-github` user-assigned managed identity for full infrastructure deployment.
3. Bind its federated credential only to the immutable Workslip repository identity and protected GitHub environment `prod`.
4. Store its client ID as the GitHub environment variable `AZURE_INFRA_CLIENT_ID`; no client secret or publish profile is created.
5. Use `deploy.ps1` as the single operator entry point. It deploys the ordinary environment first and then idempotently reconciles the infrastructure identity, federation, permissions and GitHub variable with the already-authorized human Azure/Entra session.
6. Grant Azure permissions only at the required resource-group and data-plane scopes. Resource-provider registration uses a custom subscription role containing only provider read/register actions.
7. Grant the infrastructure identity these Microsoft Graph application permissions because current infrastructure code requires them:
   - `Directory.Read.All`;
   - `Group.ReadWrite.All`;
   - `AppRoleAssignment.ReadWrite.All`.
8. Keep the identity bootstrap templates and implementation script separate from `main.bicep` so the GitHub infrastructure identity does not own or mutate its own trust boundary during normal workflow execution.

## Consequences

- Production infrastructure deployment can run without stored Azure credentials.
- The package-deployment identity remains least-privileged.
- An authorized operator uses one rerunnable command, `deploy.ps1`, rather than a separate bootstrap procedure.
- The first successful run establishes the GitHub trust boundary; later runs reconcile the same resources without duplication.
- Azure and Graph permission propagation can delay the first GitHub-hosted run.
- GitHub CLI authentication is required for local full deployment because the client ID variable is reconciled automatically.
- Removing or rotating the infrastructure identity requires coordinated GitHub variable, federation, Azure RBAC and Graph assignment updates.

## Rejected alternatives

- Reuse and widen `AZURE_CLIENT_ID`: rejected because package deployment and infrastructure administration have different trust boundaries.
- Store an Azure client secret: rejected because GitHub OIDC provides short-lived federated authentication without standing credentials.
- Grant subscription `Contributor`: rejected because only resource-provider registration requires subscription scope; all other Azure access is scoped lower.
- Put the privileged identity in ordinary `main.bicep`: rejected because a normal GitHub deployment must not control the identity and federation used to authorize that same deployment.
- Require operators to run a separate bootstrap flow: rejected because `deploy.ps1` can invoke the same idempotent reconciliation after base resources exist.
