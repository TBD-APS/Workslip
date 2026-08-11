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

### Branch-local database migrations

When the API runs in `Development` and `Azure:Sql:ConnectionString` points to a provably local SQL Server target, startup automatically applies pending versioned migrations from `../infrastructure/database/migrations` before the normal database connectivity check. This keeps a developer's local schema aligned when switching to a branch that introduces database changes.

The local path accepts only localhost/loopback, `.`, `(local)`, local SQL Server instances and LocalDB. Azure SQL, LAN hosts and other remote or ambiguous targets are never auto-migrated. Existing Azure-backed Development startup therefore remains non-mutating unless its SQL connection is explicitly overridden to a local database.

`Workslip:ApplyLocalMigrations=false` disables local auto-migration. `Workslip:ApplyLocalMigrations=true` is a strict safety assertion: startup fails if the configured target is not recognized as local instead of applying migrations remotely.

For example, on a machine with SQL Server LocalDB, keep the connection override in ignored local configuration or an environment variable:

```powershell
$env:Azure__Sql__ConnectionString='Server=(localdb)\MSSQLLocalDB;Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true'
dotnet run --launch-profile http
```

The migration history, checksum immutability, transaction and application-lock contract is shared with production migrations. Development auto-migration does not enable data seeding or Entra provisioning.

## Development database seeding

Development seeding is explicit opt-in and is database-only by default. `Workslip:SeedDevelopmentData=true` seeds the existing synthetic Workslip development dataset without resolving or invoking the Entra Superadmin provisioning service.

From macOS/Linux:

```bash
Workslip__SeedDevelopmentData=true dotnet run --launch-profile http
```

From PowerShell:

```powershell
$env:Workslip__SeedDevelopmentData="true"
dotnet run --launch-profile http
```

Entra identity reconciliation has a separate fail-closed gate. It runs only when both development-data seeding and the Entra flag are explicitly enabled:

```text
Workslip:SeedDevelopmentData=true
Workslip:SeedDevelopmentEntraIdentities=true
```

Do not enable `Workslip:SeedDevelopmentEntraIdentities` for ordinary local database work. When platform identities are the actual operation being performed, prefer the explicit `bootstrap-superadmins` command below.

Neither seed flag enables seeding outside the Development environment.

## Explicit platform Superadmin bootstrap

The three permanent platform Superadmins are reconciled through the explicit
`bootstrap-superadmins` operation, or through the separately gated development Entra
seed described above. Normal production startup does not run either path unless the
corresponding explicit operation/configuration is supplied.
The operation verifies database connectivity, reuses or invites Entra guests through
the existing Graph integration, and reconciles only the platform organization and
Superadmin rows. It does not run schema migrations, demo/reference-data seeding,
hosted workers, or the HTTP server.

From an operator workstation, first verify the Azure CLI tenant, subscription, user,
and the configured App Configuration endpoint. The current Development settings use
the production App Configuration endpoint; override it explicitly when targeting a
different approved environment.

```powershell
az login
az account show --query "{tenantId:tenantId,subscriptionId:id,user:user.name}" --output table

dotnet run --configuration Release --no-launch-profile -- `
  --environment Development `
  --Workslip:Operation=bootstrap-superadmins
```

The operator identity needs read access to App Configuration and Key Vault, database
access, and the existing Graph permissions needed to read/invite users and assign the
API `Superadmin` app role. Stop on a tenant, subscription, database, or configuration
mismatch; do not compensate with manual SQL or a normal frontend invitation.

A successful run logs exactly three `Platform Superadmin reconciled` messages and
exits without starting the API. Run the same command a second time immediately. All
three messages must report `EntraIdentityCreated: False`; the second run must not add
Workslip rows or Entra guests. On failure, preserve the logs without personal data,
fix the reported access/data conflict, and rerun the same idempotent operation. Newly
created guests are removed automatically when a later bootstrap step fails.

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

Persistence uses EF Core/SQL Server. Staging and production startup only verify database connectivity; they do not apply schema changes, backfill tenant data or run development seeding. Production schema changes require the explicit protected deployment migration operation.

Development startup may apply pending versioned migrations only when the configured SQL target is provably local, as described above. Development database seeding remains a separate explicit `Workslip:SeedDevelopmentData` opt-in, and external Entra identity reconciliation additionally requires `Workslip:SeedDevelopmentEntraIdentities`. New-tenant reference data is provisioned only during explicit organization onboarding. Treat changes to this area as production-sensitive and validate relational behaviour, concurrency and rollback explicitly.

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
