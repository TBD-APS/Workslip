# Workslip API

**Status:** Active  
**Source of truth:** backend source, tests, runtime OpenAPI and `../infrastructure/`

ASP.NET Core .NET 10 API split into API host, application, domain, infrastructure and tests.

## Prerequisites

- .NET SDK 10
- a local SQL Server target for normal `Development` startup
- Azure credentials only when an explicit operator operation or another Azure integration is intentionally used

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

`appsettings.Development.json` is tracked and intentionally contains only a safe Development baseline. It does not contain a production App Configuration endpoint, a production SQL target or machine-specific credentials. Normal `Development` startup fails closed unless the effective `Azure:Sql:ConnectionString` points to a provably local SQL target.

Put machine-specific local settings and secrets in ignored `appsettings.Local.json`, environment variables or command-line overrides. Example `appsettings.Local.json` shape:

```json
{
  "Azure": {
    "Sql": {
      "ConnectionString": "<your local SQL connection string>"
    }
  }
}
```

Do not put production/Azure SQL connection strings in normal local Development configuration.

### Branch-local database migrations

When the API runs in `Development` and `Azure:Sql:ConnectionString` points to a provably local SQL Server target, startup automatically applies pending versioned migrations from `../infrastructure/database/migrations` before the normal database connectivity check. This keeps a developer's local schema aligned when switching to a branch that introduces database changes.

The local path accepts only localhost/loopback, `.`, `(local)`, local SQL Server instances and LocalDB. Azure SQL, LAN hosts and other remote or ambiguous targets are rejected for normal Development startup, not merely skipped for migrations.

`Workslip:ApplyLocalMigrations=false` disables local auto-migration. The tracked Development baseline enables it by default; the locality check remains the hard safety boundary.

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
WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL=<approved temporary Superadmin identity>
```

The Entra-enabled development seed reuses the same rotatable platform-Superadmin bootstrap described below; it does not maintain a second list of fixed Superadmins.

Do not enable `Workslip:SeedDevelopmentEntraIdentities` for ordinary local database work. When platform identity reconciliation is the actual operation being performed, prefer the explicit `bootstrap-superadmins` command below.

Neither seed flag enables seeding outside the Development environment.

## Explicit platform Superadmin bootstrap

Workslip has one rotatable platform Superadmin identity. Its login email is supplied through `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL`, the same configuration boundary used by the synthetic release-test role identities. No personal Superadmin email is hardcoded in the bootstrap and there is no fallback identity.

The explicit `bootstrap-superadmins` operation:

- fails closed if `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL` is missing or malformed;
- reuses or invites the configured Entra guest through the existing Graph integration;
- assigns the API `Superadmin` app role to that identity;
- reconciles exactly one Workslip platform-Superadmin row under the reserved platform organization;
- refuses to promote or move an ordinary Workslip user whose email happens to match the configured value;
- refuses rotation when a legacy bootstrap identity has tenant-bound operational references;
- revokes the API `Superadmin` app role from known legacy bootstrap identities and removes their stale platform rows when migration is safe.

Normal production startup does not run this operation. The command does not run schema migrations, demo/reference-data seeding, hosted workers, or the HTTP server.

`bootstrap-superadmins` is the only current Development-mode exception that may intentionally use remote SQL. From an operator workstation, first verify the Azure CLI tenant, subscription and user, then supply the approved App Configuration endpoint and the approved temporary Superadmin identity through process-local configuration. The repository must not contain the actual identity value.

```powershell
az login
az account show --query "{tenantId:tenantId,subscriptionId:id,user:user.name}" --output table

$env:Azure__AppConfiguration__Endpoint='<approved App Configuration endpoint>'
$env:WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL='<approved temporary Superadmin identity>'

dotnet run --configuration Release --no-launch-profile -- `
  --environment Development `
  --Workslip:Operation=bootstrap-superadmins

Remove-Item Env:WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL
```

The operator identity needs read access to App Configuration and Key Vault, database access, and the existing Graph permissions needed to read/invite users, assign the API `Superadmin` app role and remove stale Superadmin app-role assignments. Stop on a tenant, subscription, database, configuration, identity-ownership or tenant-reference mismatch; do not compensate with manual SQL or a normal frontend invitation.

A successful run logs one `Rotatable platform Superadmin reconciled` message and exits without starting the API. Run the same command a second time immediately. The second run must report `EntraIdentityCreated: False`, must keep exactly one Workslip platform-Superadmin row and must not add an Entra guest.

To rotate the login identity, change only `WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL` to another approved non-permanent Superadmin test identity and run the same explicit bootstrap again. The operation removes the old synthetic identity's Superadmin app-role assignment before converging the platform database to the replacement identity. Normal login remains the supported one-time-code flow; this operation does not create a static token, alternate login endpoint or frontend role override.

On failure, preserve logs without personal data, correct the reported access/data conflict and rerun the explicit operation. A newly-created configured guest is removed automatically when a later bootstrap step fails. Failures never fall back to a personal or permanent Superadmin account.

Do not commit connection strings, JWT signing secrets, Azure credentials, VAPID private keys, integration tokens or synthetic identity email values.

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

Production/staging may use Azure App Configuration for shared non-secret runtime configuration and Key Vault references for secrets. Normal local Development starts from the tracked safe `appsettings.Development.json` baseline, then applies ignored `appsettings.Local.json`, environment variables and command-line overrides. A remote SQL target is rejected for ordinary Development startup even if Azure configuration was explicitly loaded.

Infrastructure ownership and deployment details live in [`../infrastructure/README.md`](../infrastructure/README.md).

`/api/dev/*` and `UseDeveloperExceptionPage` are ASP.NET Development-only. OpenAPI and Scalar are controlled separately by the resolved release-testing policy and can be enabled outside Development only while that policy explicitly allows it. Treat the current release policy/configuration as authoritative rather than assuming any of these surfaces are always present.

## Authentication and tenancy

The API accepts Workslip local JWTs and Microsoft Entra JWTs through the configured authentication pipeline.

Tenant/organization authority comes from authenticated server context and server-owned data. Client-provided IDs, route state or frontend guards must not create a tenant bypass.

Superadmin delegated organization access uses the existing server-side organization-session flow; verify current token lifetime and behaviour in the implementation/configuration when changing it instead of copying those values here.

## Persistence and schema

Persistence uses EF Core/SQL Server. Staging and production startup only verify database connectivity; they do not apply schema changes, backfill tenant data or run development seeding. Production schema changes require the explicit protected deployment migration operation.

Normal Development startup requires a provably local SQL target and may apply pending versioned migrations to that local database as described above. Development database seeding remains a separate explicit `Workslip:SeedDevelopmentData` opt-in, and external Entra identity reconciliation additionally requires `Workslip:SeedDevelopmentEntraIdentities`. New-tenant reference data is provisioned only during explicit organization onboarding. Treat changes to this area as production-sensitive and validate relational behaviour, concurrency and rollback explicitly.

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
