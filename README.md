# Workslip

Workslip is a React/Vite frontend with an ASP.NET Core API and Azure infrastructure.

This README is the repository entrypoint. Detailed implementation behaviour belongs in code, tests, ADRs and the maintained docs linked below rather than in a second copy here.

## Repository map

```text
src/FE/                  React frontend
src/BE/WorkslipApi/      .NET API, application/domain/infrastructure and tests
src/BE/infrastructure/   Azure/Entra infrastructure and deployment scripts
Docs/                    maintained architecture, API, operations and compliance docs
tools/                   repository maintenance and validation tooling
```

## Start locally

On a supported Windows developer machine, use the canonical full-stack bootstrap from the repository root:

```powershell
.\dev.ps1
```

For phone testing on the same trusted Wi-Fi/LAN:

```powershell
.\dev.ps1 -Mobile
```

`-Mobile` prints the LAN URL to open on the phone, exposes only the Vite development server to the LAN, and keeps the API and LocalDB local to the PC behind Vite's `/api` proxy. See [`Docs/operations/local-development.md`](Docs/operations/local-development.md) for prerequisites, useful modes, firewall guidance and the HTTP/HTTPS mobile-testing boundary.

Default local URLs:

- frontend: `http://127.0.0.1:5270`
- API: `http://localhost:5262`
- health: `http://localhost:5262/health`

Backend-only and frontend-only manual commands remain documented in the scoped READMEs for focused debugging. They do not replace the canonical full-stack bootstrap.

Environment-specific Azure credentials/configuration are described in the backend and infrastructure READMEs.

## Validation

Backend:

```bash
cd src/BE/WorkslipApi
dotnet build Workslip.slnx --configuration Release
dotnet test Workslip.slnx --configuration Release
```

Frontend:

```bash
cd src/FE
npm ci
npm run lint
npm run test -- --run
npm run build
```

Documentation:

```bash
python tools/docs/check_docs.py
```

Use [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md) to choose additional integration or Playwright validation based on the changed risk.

## Documentation

Start at [`Docs/README.md`](Docs/README.md).

High-value entrypoints:

- [`AGENTS.md`](AGENTS.md) — repository-wide implementation rules
- [`Docs/architecture/README.md`](Docs/architecture/README.md) — architecture and ADRs
- [`Docs/api/README.md`](Docs/api/README.md) — API contract and integration guidance
- [`Docs/operations/ci-quality-gates.md`](Docs/operations/ci-quality-gates.md) — CI/release expectations
- [`src/FE/README.md`](src/FE/README.md) — frontend setup and runtime notes
- [`src/BE/WorkslipApi/README.md`](src/BE/WorkslipApi/README.md) — backend setup and runtime notes
- [`src/BE/infrastructure/README.md`](src/BE/infrastructure/README.md) — Azure deployment and operations

## Truth model

Current code, checked-in configuration, schema/mappings and executable tests define implemented technical behaviour. Active ADRs and maintained docs explain decisions and operations. Linear defines work scope/status. Historical plans are context only.

If a maintained document contradicts the implementation, treat that as documentation drift and fix it rather than creating another explanatory file.
