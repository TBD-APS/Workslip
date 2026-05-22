# Azure infrastructure

Current stack:
- Blob Storage
- Logic Apps + workflow under /logic-app/workflow.json
- Application Insights
- Document Intelligence
- Azure SQL Serverless
- Azure App Configuration
- API App Service for `Workslip.Api`


Important:
- Keep costs low - always implement free services if possible.
- Prefer consumption/serverless
- Avoid dedicated App Service Plans
- Keep current structure and reuse params if possible

## Integration test environment

RBJ-25 integration testing uses a dedicated non-production API deployment plus an isolated SQL database. Keep it separate from production data.

Minimum contract:

- API base URL is stored outside source as `WORKSLIP_INTEGRATION_BASE_URL`.
- Database is an integration/staging database only.
- Reset can be done by dropping/recreating the test database before validation; `WorkslipSchemaRunner` bootstraps schema and taxonomy on API startup.
- Postman/Newman runs from `src/BE/WorkslipApi/Postman/run-integration-tests.sh`.
- GitHub Actions workflow `.github/workflows/integration-tests.yml` can run the suite manually against the configured base URL.

Do not commit connection strings, API keys, or production URLs in parameter files. Boring rule, expensive bug if ignored.


## Runtime configuration

`Workslip.Api` reads Azure App Configuration only when `AZURE_APP_CONFIG_ENDPOINT` or `AzureAppConfiguration:Endpoint` is set. The API uses `DefaultAzureCredential`; Azure deployments set `AZURE_CLIENT_ID` so the user-assigned managed identity is used.

Store secrets in Key Vault and reference them from Azure App Configuration. The shared managed identity gets `App Configuration Data Reader` on the config store and `Key Vault Secrets User` on the vault. Do not commit secret values or connection strings in parameter files. Ja, stadig ikke.
