# Internal deployment helpers

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** the three supported entry points in the parent directory  
**Review cadence:** whenever deployment entry points or helper boundaries change  
**Linear:** WOR-190

Files in this directory are implementation details invoked by `deploy.ps1`, `deploy-entra.ps1` or `deploy-infrastructure.ps1`.

- `remove-legacy-oauth-client-secret.ps1` removes only the exact obsolete deployment-created OAuth credential.
- `grant-web-api-sql-access.ps1` provisions the API managed identity in Azure SQL and owns its temporary firewall-rule lifecycle. It resolves the Azure CLI executable directly so PowerShell aliases, functions and stale session state cannot intercept its commands.

Do not run these helpers as operator deployment entry points and do not reference them directly from CI/CD workflows.
