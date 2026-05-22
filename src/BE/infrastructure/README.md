# Azure infrastructure

Current stack:
- Blob Storage
- Logic Apps + workflow under /logic-app/workflow.json
- Application insigths
- Document Intelligence
- Azure SQL Serverless


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
