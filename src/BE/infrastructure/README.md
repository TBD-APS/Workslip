# Azure infrastructure

Current stack:
- Blob Storage
- Logic Apps + workflow under /logic-app/workflow.json
- Application Insights
- Document Intelligence
- Azure App Configuration


Important:
- Keep costs low - always implement free services if possible.
- Prefer consumption/free tiers
- Keep current structure and reuse params if possible

## Runtime configuration

Runtime workloads read Azure App Configuration only when `AZURE_APP_CONFIG_ENDPOINT` or `AzureAppConfiguration:Endpoint` is set. Set `AZURE_CLIENT_ID` and `AZURE_APP_CONFIG_ENDPOINT` on the existing runtime outside this template.

Store secrets in Key Vault and reference them from Azure App Configuration. This template grants the shared managed identity `App Configuration Data Reader` on the config store and keeps the existing `Key Vault Secrets User` access on the vault. Do not commit secret values or connection strings in parameter files. Ja, stadig ikke.
