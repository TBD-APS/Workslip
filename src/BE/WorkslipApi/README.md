# Workslip.Api

Workslip.Api er backend-indgangen til Workslip-produktet.

Navnet er bevidst bredere end det gamle `DocumentApi`, fordi backend'en ikke kun skal håndtere dokumenter. Den skal være produktets API for:

- jobs og digitale arbejdssedler
- organisationer
- brugere/login og roller
- attestering og revisionsspor
- sagslinkning
- afvigelser
- faktura-/fakturaklarhedsdata
- senere eksport/PDF og eventuelle dokumentmoduler

## Aktiv MVP-retning

MVP'en er jobs-baseret:

- API-sproget er `jobs`, ikke `workslips`.
- Aktive endpoints ligger under `/api/jobs`.
- Den primære persistensmodel er `JobReports`, `JobControlChecks` og `JobEvents`.
- Generiske dokumentmodeller er ikke aktiv MVP-kontrakt.

## Solution structure

```text
WorkslipApi/
  Workslip.slnx
  Workslip.Api.csproj
  Program.cs
  Endpoints/
    JobEndpoints.cs
    OrganizationEndpoints.cs
    AuthEndpoints.cs
  Workslip.Domain/
    Workslip.Domain.csproj
  Workslip.Application/
    Workslip.Application.csproj
    Jobs/
  Workslip.Infrastructure/
    Workslip.Infrastructure.csproj
    Repositories/
    Models/
    Schema/
```

## Current active endpoints

### Health

- `GET /health`

### Organizations

- `POST /api/organizations`

### Auth/current user

- `GET /api/auth/me?userId={userId}`

### Jobs

- `POST /api/jobs`
- `GET /api/jobs`
- `GET /api/jobs/{id}`
- `PATCH /api/jobs/{id}`
- `POST /api/jobs/{id}/submit`
- `POST /api/jobs/{id}/approve`
- `POST /api/jobs/{id}/reject`

## Work kind mapping

`JobReports.WorkKind` stores the selected frontend arbejdstype id.

Allowed values match the deployed customer PWA:

- `nyInstallation` = Ny installation
- `aendring` = Ændring af installation
- `reparation` = Reparationsarbejde
- `serviceAndet` = Andet

`JobReports.CustomWorkKind` stores the PWA/API `customWorkKind` free-text value. It is only valid when `workKind` is `serviceAndet`; for the other work kinds it must be omitted/null. There is no `CustomerWorkKind` field in the active backend model.

## Naming rules

Use these names consistently:

| Layer | Name |
|---|---|
| Solution | `Workslip.slnx` |
| API project | `Workslip.Api` |
| Domain project | `Workslip.Domain` |
| Application project | `Workslip.Application` |
| Infrastructure project | `Workslip.Infrastructure` |
| Repository | `DapperJobRepository` |
| Main table | `dbo.JobReports` |
| Control checks table | `dbo.JobControlChecks` |
| Event table | `dbo.JobEvents` |
| Public route | `/api/jobs` |

Avoid reintroducing the old document-centric API name, the old workslip route, the old workslip table names, or the old workslip repository name.

## Configuration

Connection string lookup currently supports:

- `ConnectionStrings:JobDB`
- `Sql:ConnectionString`

Local development can use SQL Server LocalDB or another SQL Server-compatible connection string.

Azure deployments can load centralized configuration from Azure App Configuration when either of these values is set:

- `AZURE_APP_CONFIG_ENDPOINT`
- `AzureAppConfiguration:Endpoint`

The API uses `DefaultAzureCredential`. Azure App Service sets `AZURE_CLIENT_ID` for the user-assigned managed identity; local development can use developer credentials instead. Key Vault references in App Configuration are resolved with the same credential, so secrets stay out of source and App Service settings.

Logging uses Serilog:

- Console logging is configured through `Serilog:WriteTo`.
- Request logging is enabled through `UseSerilogRequestLogging()`.
- Application Insights logging uses the deployed `APPLICATIONINSIGHTS_CONNECTION_STRING` setting.

## Database schema

Database schema is generated from code models in `Workslip.Infrastructure/Models` and applied by `Workslip.Infrastructure/Schema/WorkslipSchemaRunner`.

The active MVP schema creates the jobs-oriented tables:

- `Organizations`
- `Users`
- `Organizations` with unique 8-digit `Cvr`
- `Users` with organization-scoped roles
- `JobReports`
- `JobControlChecks`
- `JobEvents`

## Integration tests

Postman/Newman integration tests live under `Postman/`.

Run against a deployed non-production API:

```bash
src/BE/WorkslipApi/Postman/run-integration-tests.sh https://<staging-api-base-url>
```

CI workflow: `.github/workflows/integration-tests.yml`.

Required setting outside source:

- `WORKSLIP_INTEGRATION_BASE_URL`: staging/test API base URL.

The Postman collection generates unique per-run organization/job data so repeated deploy tests do not collide on CVR/report numbers. The target database must still be isolated from production and resettable through environment recreation/drop-create when release validation needs a clean slate.
