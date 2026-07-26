# ADR 0001: Azure SQL managed identity and App Service IP allowlist

- **Status:** Accepted
- **Date:** 2026-07-26
- **Owner:** Workslip maintainers
- **Decision issue:** [WOR-137](https://linear.app/workslip/issue/WOR-137/go-live-las-azure-sql-ned-til-managed-identity-og-begraenset-netvaerk)
- **Review cadence:** Before go-live and whenever the App Service plan or SQL network topology changes

## Context

The production template previously stored a SQL administrator username/password connection string in Key Vault, allowed SQL authentication, enabled the `0.0.0.0` "Allow Azure services" firewall rule and kept a fixed developer IP open.

The API currently runs on the Free F1 App Service plan. App Service VNet integration, which is required for reaching an Azure SQL private endpoint, starts at the Basic tier. Moving to Private Link now would therefore also require a hosting-plan upgrade and additional VNet/DNS infrastructure.

## Decision

Workslip uses:

- a user-assigned runtime identity for the API;
- a separate user-assigned deployment identity for GitHub OIDC, Web App deployment and SQL control-plane/provisioning operations;
- Microsoft Entra-only authentication on Azure SQL;
- a non-secret App Configuration connection string using `Active Directory Managed Identity` and the runtime identity client ID;
- the Azure SQL public endpoint with server firewall rules restricted to the API App Service's complete `possibleOutboundIpAddresses` set;
- an active database readiness endpoint plus a deployment validation that proves the API can connect and an external GitHub runner is rejected by the SQL firewall.

The runtime identity is a contained database user. It receives `db_datareader` and `db_datawriter`. It temporarily retains `db_ddladmin` because the current API startup still applies schema changes. [WOR-162](https://linear.app/workslip/issue/WOR-162/etabler-ef-core-migrations-og-genereret-database-deploy-for-go-live) must remove runtime DDL access when migrations move into the approved deployment job.

## Consequences

- There is no reusable SQL password in runtime configuration or deployment parameters.
- SQL authentication is disabled server-wide.
- "Allow Azure services", fixed developer IPs and unmanaged server firewall rules are removed on infrastructure deployment.
- App Service scale/tier changes can add possible outbound IPs. Redeploy the infrastructure before changing the plan so the allowlist is refreshed.
- Direct developer access requires a deliberate, temporary and separately approved access path. It is not persisted in Bicep.
- The public SQL endpoint remains enabled until the hosting plan supports VNet integration. Authentication and an exact IP allowlist provide the current controls.

## Upgrade trigger

When the API moves to Basic or higher, reassess this ADR. The preferred next step is App Service VNet integration, an Azure SQL private endpoint, private DNS and `publicNetworkAccess: 'Disabled'`.

## References

- [Microsoft Entra-only authentication for Azure SQL](https://learn.microsoft.com/azure/azure-sql/database/authentication-azure-ad-only-authentication)
- [SqlClient managed identity authentication](https://learn.microsoft.com/sql/connect/ado-net/sql/azure-active-directory-authentication)
- [Azure SQL network access controls](https://learn.microsoft.com/azure/azure-sql/database/network-access-controls-overview)
- [App Service outbound IP addresses](https://learn.microsoft.com/azure/app-service/overview-inbound-outbound-ips)
- [App Service VNet integration tiers](https://learn.microsoft.com/azure/app-service/overview-vnet-integration)
