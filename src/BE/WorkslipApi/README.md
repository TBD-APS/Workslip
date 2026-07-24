# Workslip API

ASP.NET Core .NET 10 API for Workslip.

The solution is split into `Workslip.Domain`, `Workslip.Application`, `Workslip.Infrastructure`, the API host and tests. Current code and executable tests take precedence over dated plans.

## Prerequisites

- .NET SDK 10
- SQL Server-compatible database
- Azure credentials when configuration enables Azure App Configuration, Key Vault, Microsoft Graph, Application Insights or Azure-hosted integrations

## Local start

```bash
dotnet restore
dotnet run --launch-profile http
```

The HTTP profile listens on `http://localhost:5262`.

Required database configuration:

```text
Azure:Sql:ConnectionString
```

Environment-variable form:

```text
Azure__Sql__ConnectionString
```

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
├── Configuration/                 # host, auth, pipeline, services and endpoints
├── Endpoints/                     # minimal API route registration
├── Workslip.Domain/               # domain enums and data models
├── Workslip.Application/          # services, contracts, validators and ports
├── Workslip.Infrastructure/       # EF Core, repositories, integrations and workers
├── Workslip.Tests/                # automated tests
└── Postman/                       # non-production integration suite
```

## Runtime composition

`Program.cs` performs these steps:

1. Load infrastructure configuration, including optional Azure App Configuration and Key Vault references.
2. Configure CORS, authentication, logging, application and infrastructure services.
3. Initialize the database schema and verify connectivity.
4. Seed development data only when `ASPNETCORE_ENVIRONMENT=Development`.
5. Configure middleware and map endpoints.

Persistence uses EF Core `SqlDbContext` with SQL Server, repository implementations and an audit interceptor. Migrations are configured in the API assembly. Hosted services currently include job-deletion cleanup, invitation/Entra cleanup and push-notification delivery.

## API areas

`Configuration/EndpointConfiguration.cs` maps:

- `GET /health`
- organizations
- authentication/current user
- users and invitations
- jobs and job links
- customers
- worksheets
- reference data
- push notifications
- cache operations

Use the runtime OpenAPI document and endpoint files for exact routes, request/response models and permissions. Do not use this README as a frozen endpoint list.

## Authentication and authorization

The API selects between a local JWT scheme and Microsoft Entra JWT based on the bearer-token issuer. Authorization is enforced server-side through ASP.NET Core policies and Workslip's dynamic role/permission handling.

Tenant/organization identifiers must come from authenticated server context or server-owned data. Frontend route guards are not security boundaries.

Developer token/debug endpoints are intended only for Development. Their environment guard is security-sensitive and must remain covered by review/tests.

## Result and error conventions

Application services return `Ardalis.Result`. Endpoints use `ResultExtensions.ToHttpResult` rather than defining custom wrappers or inline HTTP mappings.

Common mapping:

| Result | HTTP |
|---|---:|
| Success | 200 |
| Invalid | 400 validation problem |
| Unauthorized | 401 |
| Forbidden | 403 |
| Not found | 404 |
| Conflict | 409 |
| No content | 204 |
| Unexpected result | 500 |

See the root `AGENTS.md` before changing service or endpoint patterns.

## Correlation and idempotency

The pipeline assigns/logs correlation identifiers. Frontend mutations send `X-Correlation-ID` and `Idempotency-Key`; only endpoints/services that explicitly use the idempotency infrastructure should be described as idempotent. A header alone is not proof of durable retry safety.

## Database lifecycle

The application initializes schema at startup through `DatabaseSchemaInitializer`, then verifies connectivity. Treat schema initialization and EF migrations as production-impacting operations:

- inspect generated SQL and migration history;
- use an isolated environment first;
- define rollback/roll-forward before deployment;
- never run destructive database actions without explicit approval.

## Build and tests

```bash
dotnet build Workslip.slnx
dotnet test Workslip.slnx
```

Integration tests use Postman/Newman and must target localhost, test or staging:

```bash
Postman/run-integration-tests.sh https://<test-or-staging-api>
```

The script rejects URLs that do not look non-production unless `ALLOW_PRODUCTION_INTEGRATION_TESTS=true` is explicitly set. Do not bypass that protection during ordinary validation.

GitHub workflow: `.github/workflows/integration-tests.yml`.

## OpenAPI and Scalar

The host registers ASP.NET Core OpenAPI and Scalar. These surfaces must be treated as non-production developer/integration tooling unless production exposure is explicitly approved and protected. The maintained contract guide lives in `../../../Docs/api/README.md`.

## Deployment

The API deployment workflow is `.github/workflows/main_api-npteknik-prod.yml`. It builds and publishes the .NET project, authenticates to Azure through OIDC and deploys to an Azure Web App.

Workflow definitions are evidence of intended automation, not evidence that a deployment or rollback has succeeded. Record actual release validation separately.
