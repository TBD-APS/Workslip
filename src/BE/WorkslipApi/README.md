# Workslip API

**Status:** Active  
**Owner:** Backend owner  
**Source of truth:** backend source, executable tests, runtime OpenAPI and `../infrastructure/`  
**Review cadence:** on API, persistence, authentication or deployment changes

ASP.NET Core .NET 10 API for Workslip. The solution is split into domain, application, infrastructure, API host and tests. Current code and executable tests take precedence over dated plans.

## Prerequisites

- .NET SDK 10
- SQL Server-compatible database
- Azure credentials only when configuration enables Azure App Configuration, Key Vault, Microsoft Graph or Azure-hosted integrations

## Local start

```bash
cd src/BE/WorkslipApi
dotnet restore
dotnet run --launch-profile http
```

The HTTP profile listens on `http://localhost:5262`.

Local development can use the production database location and passwordless connection details from Azure App Configuration and Key Vault. No SQL connection string or SQL password is required in local configuration.

Authenticate the developer identity first:

```powershell
az login
az account show
```

The configured identity must be permitted to connect to the Azure SQL database. Members of the configured Azure SQL administrator group can connect through their Entra identity.

The Key Vault-backed value remains:

```text
Azure:Sql:ConnectionString
```

Production keeps `Authentication=Active Directory Managed Identity` and the App Service managed-identity client ID. During Development, the API changes only the in-memory authentication fields to:

```text
Authentication=Active Directory Default
```

The managed-identity `User ID` is removed locally, allowing SqlClient to use the signed-in Azure CLI, Visual Studio or other `DefaultAzureCredential` developer identity. Server name, database name, encryption settings and the Key Vault/App Configuration ownership remain unchanged. Nothing is written back to Azure.

An ignored `appsettings.Development.json` value or `Azure__Sql__ConnectionString` environment variable can still override this behavior for isolated/offline database work, but neither is required for the normal Azure-backed development path.

Do not commit connection strings, JWT signing secrets, Azure credentials, VAPID private keys or integration tokens.

Health check:

```bash
curl http://localhost:5262/health
```

## Solution structure

```text
WorkslipApi/
├── Workslip.slnx
├── Workslip.Api.csproj
├── Program.cs
├── Configuration/
├── Endpoints/
├── Workslip.Domain/
├── Workslip.Application/
├── Workslip.Infrastructure/
├── Workslip.Tests/
└── Postman/
```

## Runtime composition

`Program.cs` currently:

1. loads local and Azure configuration;
2. configures CORS, authentication, logging and services;
3. initializes and verifies the database schema;
4. seeds only in Development;
5. configures middleware and maps endpoints.

Persistence uses EF Core `SqlDbContext` with SQL Server, repositories and an audit interceptor. Hosted services include job-deletion cleanup, invitation/Entra cleanup and push-notification delivery.

Production SQL authentication uses the App Service user-assigned managed identity. The passwordless connection string is stored through a Key Vault reference. The SQL administrator password is deployment-only and must not be used by application runtime configuration.

Schema mutation still occurs at startup. Treat that as a production limitation tracked by WOR-136; do not remove the temporary `db_ddladmin` runtime role until migrations have moved to a controlled deployment step.

## Authentication and authorization

The API selects local Workslip JWT or Microsoft Entra JWT validation from the bearer-token issuer. The public browser flow uses authorization code + PKCE. The API does not require an OAuth application client secret for that flow.

API runtime Microsoft Graph permissions are declared once in `../infrastructure/main.bicep`. They support user invitation/lifecycle and app-role assignment. Do not assign a competing permission set from deployment scripts.

Developer token, debug, OpenAPI and Scalar endpoints are development tooling. Their production exposure is tracked as an urgent security issue in WOR-182 and must not be used as production integration authentication.

Tenant/organization identifiers must come from authenticated server context or server-owned data. Frontend guards are not security boundaries.

## Result and endpoint conventions

Application services return `Ardalis.Result`. Endpoints map through `ResultExtensions.ToHttpResult`; do not introduce custom wrappers or duplicate result-to-HTTP mapping. See the root `AGENTS.md` before changing service or endpoint patterns.

## HTTP caching

Authenticated job GET endpoints use private browser revalidation, `Vary: Authorization` and weak ETags. Jobs-list validators are calculated from the complete mapped HTTP representation, including assignments, worksheet totals, installations and user-specific seen/rejection flags; hashing only the `JobReports.UpdatedAt` value is insufficient because those related values can change independently.

The validator is evaluated after tenant-scoped service/repository work. A `304 Not Modified` therefore saves response transfer and client parsing, but it is not a server-side query cache. Do not add shared output caching or an in-memory jobs-list cache without complete organization/user/filter keys and explicit invalidation for every job, assignment, worksheet, installation and view-state mutation. Mutation responses remain `no-store`.

## Build and tests

```bash
cd src/BE/WorkslipApi
dotnet build Workslip.slnx
dotnet test Workslip.slnx
```

Postman/Newman verification must target localhost or an isolated test/staging API:

```bash
Postman/run-integration-tests.sh https://<test-or-staging-api>
```

There is no active GitHub Actions integration-test workflow. Run the executable suite deliberately against an isolated environment; do not use production mutation tests in ordinary validation.

## OpenAPI and Scalar

The host registers ASP.NET Core OpenAPI and Scalar. Treat these as development/integration tooling unless production exposure is explicitly approved and protected. Runtime endpoint registrations are the API contract source; Postman is verification material.

## Deployment

Azure infrastructure and Entra registrations are deployed separately. See `../infrastructure/README.md`.

The production API workflow is:

```text
.github/workflows/main_api-mrsoftware-prod.yml
```

It builds and publishes the API, authenticates to Azure through GitHub OIDC and deploys to `api-mrsoftware-prod`. Production uses GitHub environment `prod` with:

- `AZURE_CLIENT_ID`: Bicep output `GITHUB_DEPLOYMENT_CLIENT_ID`
- `AZURE_TENANT_ID`: target Entra tenant ID
- `AZURE_SUBSCRIPTION_ID`: target subscription ID

Do not add an Azure client secret, `AZURE_CREDENTIALS` JSON or App Service publish profile.

After infrastructure recreation:

1. run `../infrastructure/deploy.ps1 prod`;
2. set the three GitHub `prod` environment identifiers from the deployment output;
3. run the API workflow manually;
4. verify `/health`, Microsoft login and one authenticated API request.

Workflow definitions are intended automation, not evidence that deployment, migration or rollback succeeded.
