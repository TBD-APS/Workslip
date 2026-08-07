# Workslip API

**Status:** Active  
**Source of truth:** backend source, tests, runtime OpenAPI and `../infrastructure/`

ASP.NET Core .NET 10 API split into API host, application, domain, infrastructure and tests.

## Prerequisites

- .NET SDK 10
- access to the configured SQL Server database
- Azure credentials when local configuration uses Azure App Configuration, Key Vault, Graph or other Azure integrations

## Run locally

```bash
cd src/BE/WorkslipApi
dotnet restore
dotnet run --launch-profile http
```

The HTTP profile listens on `http://localhost:5262`.

Health check:

```bash
curl http://localhost:5262/health
```

For the normal Azure-backed development path, authenticate the developer identity with Azure CLI before starting the API:

```powershell
az login
az account show
```

Do not commit connection strings, JWT signing secrets, Azure credentials, VAPID private keys or integration tokens.

## Solution map

```text
WorkslipApi/
├── Workslip.slnx
├── Program.cs
├── Configuration/
├── Endpoints/
├── Workslip.Domain/
├── Workslip.Application/
├── Workslip.Infrastructure/
├── Workslip.Tests/
└── Postman/
```

Use [`AGENTS.md`](AGENTS.md) for backend architecture rules. Application services use `Ardalis.Result`; endpoints map through `ResultExtensions.ToHttpResult`.

## Runtime configuration

Azure App Configuration owns non-secret runtime configuration. Secret values are resolved through Key Vault references/managed identity where configured. Infrastructure ownership and deployment details live in [`../infrastructure/README.md`](../infrastructure/README.md).

Development/release-test endpoints (OpenAPI, Scalar and `/api/dev/*`) are registered only when the current release-testing policy enables them. `UseDeveloperExceptionPage` remains a Development-only concern. Treat the current release policy/configuration as authoritative rather than assuming those endpoints are always present.

## Authentication and tenancy

The API accepts Workslip local JWTs and Microsoft Entra JWTs through the configured authentication pipeline.

Tenant/organization authority comes from authenticated server context and server-owned data. Client-provided IDs, route state or frontend guards must not create a tenant bypass.

Superadmin delegated organization access uses the existing server-side organization-session flow; verify current token lifetime and behaviour in the implementation/configuration when changing it instead of copying those values here.

## Persistence and schema

Persistence uses EF Core/SQL Server. Startup currently performs schema initialization/verification, so schema lifecycle remains coupled to API startup. Treat changes to this area as production-sensitive and validate relational behaviour, concurrency and rollback explicitly.

Do not infer SQL behaviour from EF in-memory tests.

## API contract and integration evidence

Start at [`../../../Docs/api/README.md`](../../../Docs/api/README.md).

- endpoint registrations + runtime OpenAPI define the route/contract source;
- Postman is executable verification/example material;
- maintained API docs explain cross-cutting semantics that OpenAPI does not express well.

## Build and tests

```bash
cd src/BE/WorkslipApi
dotnet build Workslip.slnx --configuration Release
dotnet test Workslip.slnx --configuration Release
```

Run Postman/Newman only against localhost or an isolated approved test/staging API. Do not run destructive mutation suites against customer production data.

Use [`../../../Docs/agents/VALIDATION.md`](../../../Docs/agents/VALIDATION.md) for HTTP, relational, authorization and integration validation requirements.

## Deployment

The production API workflow is `.github/workflows/main_api-mrsoftware-prod.yml`. It builds/publishes the API, authenticates through GitHub OIDC, deploys to Azure App Service and performs its health check.

A successful deployment is not proof that login, SQL access, external integrations or critical user flows work. Smoke-test the affected runtime path when deployment is part of the change.
