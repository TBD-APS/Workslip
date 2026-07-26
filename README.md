# Workslip

Workslip is a React PWA and ASP.NET Core API for jobs, digital worksheets, customers, users, approvals, reporting and related administration.

> **Documentation rule:** repository code and configuration describe implemented behaviour. Linear describes planned scope and delivery status. Planning documents are not evidence that a feature exists.

## Start here

| Need | Source of truth |
|---|---|
| Documentation map and lifecycle | [`Docs/README.md`](Docs/README.md) |
| Frontend setup and validation | [`src/FE/README.md`](src/FE/README.md) |
| Backend setup, persistence and tests | [`src/BE/WorkslipApi/README.md`](src/BE/WorkslipApi/README.md) |
| Agent and implementation conventions | [`AGENTS.md`](AGENTS.md) |
| Current implementation | Source code and configuration in `src/` |
| API contract | Runtime OpenAPI document and endpoint code; see [`Docs/api/README.md`](Docs/api/README.md) |
| Delivery scope and status | Linear workspace `Workslip` |
| Stable architecture decisions | [`Docs/architecture/README.md`](Docs/architecture/README.md) and ADRs |
| Historical plans and specifications | `Docs/superpowers/`, `.hermes/` and dated documents under `src/docs/` |

## Repository layout

```text
.
├── AGENTS.md
├── Docs/                         # maintained documentation and historical plans
├── src/
│   ├── BE/WorkslipApi/           # .NET 10 API, application, domain and infrastructure
│   ├── FE/                       # React 19 + TypeScript + Vite PWA
│   └── docs/                     # dated product and implementation plans
└── .github/workflows/            # CI, integration tests and deployment workflows
```

## Local development

### Prerequisites

- .NET SDK 10
- Node.js 22 and npm
- SQL Server-compatible database
- Azure credentials only when local configuration references Azure App Configuration, Key Vault, Graph or other Azure services

Never commit connection strings, tokens, certificates or client secrets.

### Backend

```bash
cd src/BE/WorkslipApi
dotnet restore
dotnet run --launch-profile http
```

The HTTP launch profile listens on `http://localhost:5262`. Configure the database connection through .NET configuration key `Azure:Sql:ConnectionString`; environment variables use `Azure__Sql__ConnectionString`.

Health check:

```bash
curl http://localhost:5262/health
```

Database readiness:

```bash
curl http://localhost:5262/health/ready
```

### Frontend

```bash
cd src/FE
npm ci
npm run dev
```

Vite listens on `http://127.0.0.1:5270` and proxies `/api` to `http://localhost:5262` during local development.

## Validation

```bash
# Backend
cd src/BE/WorkslipApi
dotnet build Workslip.slnx
dotnet test Workslip.slnx

# Frontend
cd src/FE
npm ci
npm run lint
npm run build
```

Postman/Newman verification must target an isolated non-production environment:

```bash
src/BE/WorkslipApi/Postman/run-integration-tests.sh https://<test-or-staging-api>
```

## Change discipline

- Follow `AGENTS.md`, including `Ardalis.Result`, `ResultExtensions.ToHttpResult` and shared frontend components.
- Use a real Linear issue and a small pull request for each coherent change.
- Update documentation in the same pull request when API, auth, infrastructure, dataflow or user behaviour changes.
- Mark proposals and historical documents explicitly; do not rewrite them as implemented behaviour.
- Do not run migrations, destructive database actions, force pushes or production changes without explicit approval.
