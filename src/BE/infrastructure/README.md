# Azure infrastructure

Current stack:
- Blob Storage
- Logic Apps + workflow under /logic-app/workflow.json
- Application Insights
- Document Intelligence
- Azure SQL Serverless
- Azure App Configuration


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

`Workslip.Api` reads Azure App Configuration only when `AZURE_APP_CONFIG_ENDPOINT` or `AzureAppConfiguration:Endpoint` is set. This template wires the existing Function App config to the shared user-assigned managed identity by setting both `AZURE_CLIENT_ID` and `AZURE_APP_CONFIG_ENDPOINT`; no new API/server hosting is created.

Store secrets in Key Vault and reference them from Azure App Configuration. This template grants the shared managed identity `App Configuration Data Reader` on the config store and keeps the existing `Key Vault Secrets User` access on the vault. Do not commit secret values or connection strings in parameter files. Ja, stadig ikke.
