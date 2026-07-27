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

## Endpoint file conventions

Each `Endpoints/*Endpoints.cs` file is a single static class with one `MapXxxEndpoints` extension method. The goal is **minimal handler bodies** — business logic lives in services, parsing/validation lives in private helpers.

### Rules

1. **One handler = one expression or a small block.** Call the service, pipe through `ResultExtensions.ToHttpResult`, done. No inline business logic.
2. **Extract complex handlers into private static helpers.** File uploads, multi-step validation, or format detection that doesn't belong in the service layer goes into a private method in the same file.
3. **Never duplicate `ToHttpResult` mapping.** Use the mapper overload — `ResultExtensions.ToHttpResult(result, ViewModelBuilder.ToXxx)`. Do not inline `Results.Ok(...)` / `Results.NotFound(...)` etc.
4. **Idempotent endpoints use `IdempotentMutationService`.** The service handles reservation start/complete/abort. The endpoint only maps `IsReplay` / `Conflict` / `InProgress` to HTTP responses.
5. **Group setup is one line per auth level.** Use `MapReadGroup`, `MapUserGroup`, `MapAdminGroup`, or `MapReadUserGroups`. Do not add manual `.RequireAuthorization(...)` unless the endpoint differs from its group default.
6. **Keep using statements minimal.** Only import what the file actually references.

### Example — simple CRUD endpoint

```csharp
adminGroup.MapPut("/{id:guid}", async (Guid id, UpdateRequest request, IService service, CancellationToken ct) =>
{
    var result = await service.UpdateAsync(id, request, ct);
    return ResultExtensions.ToHttpResult(result, ViewModelBuilder.ToDetail);
});
```

### Example — endpoint with extracted helper

```csharp
adminGroup.MapPost("/import", async (IFormFile file, IService service, ILoggerFactory logFactory, CancellationToken ct) =>
{
    var logger = logFactory.CreateLogger("Import");
    var parse = ParseImportFile(file, logger);
    if (parse.Error is not null) return parse.Error;

    var result = await service.ImportAsync(parse.Parsed!.Rows, ct);
    return ResultExtensions.ToHttpResult(result, MapImportResponse);
})
.DisableAntiforgery()
.RequireRateLimiting("import-rate-limit");

private static (IResult? Error, ParseResult? Parsed) ParseImportFile(IFormFile file, ILogger logger)
{
    // validation, format detection, try/catch parsing — returns tuple
}
```

### Anti-patterns

- `Results.Ok(result.Value)` inside an endpoint — use `ToHttpResult`.
- Inline validation/parsing exceeding ~10 lines — extract to a private helper or move to the service layer.
- Duplicating the same handler body across two routes — extract to a shared private method.
- `async` lambda with expression body when the mapper needs `.Select().ToArray()` — use a block body.

See `UserEndpoints.cs` (47 lines, 5 routes) and `CustomerEndpoints.cs` for reference implementations.

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

Production uses the protected GitHub environment `prod`. The Azure infrastructure
creates a dedicated GitHub deployment managed identity with `Website Contributor`
limited to the API App Service and a federated credential restricted to:

```text
repo:rasm105k/Workslip-v2.0:environment:prod
```

After deploying the infrastructure, configure these environment secrets in the
GitHub `prod` environment:

- `AZURE_CLIENT_ID`: deployment output `GITHUB_DEPLOYMENT_CLIENT_ID`
- `AZURE_TENANT_ID`: Microsoft Entra tenant ID
- `AZURE_SUBSCRIPTION_ID`: target Azure subscription ID

They are identifiers, not passwords. Do not add an Azure client secret,
`AZURE_CREDENTIALS` JSON or an App Service publish profile.

### Production API custom domain

The official production API origin is planned as `https://api.mrsoftware.dk`. The existing Azure default hostname remains the rollback origin during cutover.

Azure App Service custom domains are not supported on the current Free F1 plan. The infrastructure therefore stays on F1 by default and changes to paid Basic B1 only when `deploy.ps1` is run with `-EnableApiCustomDomain`. Do not use that switch without explicit approval of the recurring Azure cost.

Cutover sequence:

1. Register and control `mrsoftware.dk`.
2. Run `./deploy.ps1 -EnableApiCustomDomain`; record the printed default API hostname and verification ID.
3. In Cloudflare, create `CNAME api` to the printed `*.azurewebsites.net` hostname and `TXT asuid.api` to the printed verification ID. Keep the CNAME as DNS only while Azure validates it.
4. After public DNS resolves, run `./configure-api-custom-domain.ps1`. The script verifies DNS, adds the Azure hostname, creates an App Service managed certificate, binds SNI TLS and checks `/health`.
5. Set Vercel `VITE_API_BASE_URL` to `https://api.mrsoftware.dk` and redeploy the frontend.
6. Smoke-test login, invitation enrollment and authenticated API requests before removing the Azure default hostname from rollback documentation.

The domain script is idempotent for an existing hostname and certificate. It intentionally does not create Cloudflare records or change Vercel settings.

After recreating the Azure resource group:

1. Run `deploy.ps1` and copy the printed `GITHUB_DEPLOYMENT_CLIENT_ID`.
2. Set the three `prod` environment secrets above and configure the required reviewer.
3. Run the workflow manually once to verify the OIDC deployment.

Workflow definitions are evidence of intended automation, not evidence that a deployment or rollback has succeeded. Record actual release validation separately.
