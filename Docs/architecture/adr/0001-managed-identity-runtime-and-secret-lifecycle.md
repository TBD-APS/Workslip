# ADR 0001: Managed-identity runtime and explicit secret lifecycle

**Status:** Accepted  
**Date:** 2026-07-27  
**Owner:** Workslip architecture owner  
**Linear:** WOR-190

## Context

The infrastructure rewrite separated Entra application registration from Azure resources, but the infrastructure wrapper still delegated to a monolithic legacy script. That script duplicated Graph permission assignment, provisioned an unused OAuth client secret and configured the API to use the SQL administrator password at runtime. JWT signing material was deterministic and derived from public resource identifiers.

The API already has a user-assigned managed identity. The browser uses authorization code + PKCE, and the API validates bearer tokens; no confidential OAuth client credential is required by the implemented flow.

Vercel cache purge is an external frontend-hosting operation. It is not part of Azure resource provisioning and must not make an infrastructure deployment depend on Vercel credentials.

Multiple overlapping wrapper scripts also made it unclear which deployment command an operator should run. The production ACS custom domain is fully verified, so an operator-controlled activation toggle would preserve obsolete rollout state and create configuration drift.

## Decision

1. `deploy-entra.ps1` is the only application-registration reconciliation phase.
2. `deploy-infrastructure.ps1` deploys Azure resources directly and never invokes the legacy monolithic implementation.
3. `deploy.ps1` is the single full-deployment entry point and runs Entra reconciliation, exact legacy OAuth credential cleanup and Azure infrastructure deployment in that order.
4. No additional public deployment wrapper is retained. Helper scripts are internal implementation details.
5. `main.bicep` is the single source of truth for API runtime Microsoft Graph app-role assignments.
6. Production API SQL connections authenticate with the user-assigned managed identity and its client ID. SQL authentication remains only as a controlled deployment bootstrap for creating the contained database principal.
7. App Configuration contains versionless Key Vault references for JWT signing material and the SQL connection string.
8. Vercel cache-purge credentials and project configuration remain outside the Azure infrastructure deployment boundary.
9. JWT signing material is generated with a cryptographic random-number generator and rotated explicitly or when the legacy deterministic value is detected.
10. The deployment-created OAuth application client secret is removed. Future server-side confidential-client flows require a separate ADR and scoped credential lifecycle.
11. Every supported Azure infrastructure deployment selects the verified `mrsoftware.dk` ACS email domain, provisions `noreply`, and writes `noreply@mrsoftware.dk` to App Configuration. The deployment entry points expose no activation parameter. The Azure-managed domain remains linked only as an emergency rollback resource.

## Required Graph permissions

The API runtime identity receives only the permissions demonstrated by current code:

- `User.ReadWrite.All`
- `User.Invite.All`
- `Application.Read.All`
- `AppRoleAssignment.ReadWrite.All`

Deployment scripts do not assign a second competing set.

## Consequences

- Operators have three supported commands: one full deployment and one command for each phase.
- Runtime SQL access no longer depends on a long-lived SQL administrator password.
- SqlClient Entra authentication requires `Microsoft.Data.SqlClient.Extensions.Azure` alongside SqlClient 7.x.
- Recreating the managed identity changes its client ID; SQL provisioning must replace a stale contained user SID.
- JWT rotation invalidates outstanding local Workslip JWTs.
- Operators must preserve access to the deployment-only SQL administrator secret and install `sqlcmd`.
- `db_ddladmin` remains temporarily assigned because schema mutation still occurs during API startup. WOR-136 must remove startup migration and then remove this role.
- Vercel cache purge must be configured and operated separately from Azure infrastructure deployment.
- The production custom email domain must remain verified; normal deployment no longer offers a sender-selection toggle.
- Production execution, secret migration and smoke-test evidence remain operator responsibilities after merge.

## Rejected alternatives

- Keep SQL administrator credentials in the runtime connection string: rejected because it grants unnecessary standing privilege.
- Store JWT values directly in App Configuration: rejected because App Configuration is the non-secret configuration layer.
- Make Azure infrastructure deployment require a Vercel token: rejected because cache purge is an optional external operation with a separate lifecycle.
- Keep `deploy-safe.ps1` as a second full-deployment wrapper: rejected because overlapping entry points create operator ambiguity.
- Keep an operator ACS custom-domain activation parameter after DNS verification: rejected because permanent production configuration should be identical on every deployment.
- Keep a long-lived OAuth client secret “for later”: rejected because no implemented confidential-client flow consumes it.
- Assign Graph permissions from both Bicep and PowerShell: rejected because drift and partial deployment make the effective permission set unclear.
