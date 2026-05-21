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
  Workslip.sln
  Workslip.Api.csproj
  Program.cs
  Endpoints/
    JobEndpoints.cs
    DocumentEndpoints.cs        # parkeret/future-scope; ikke mappet i Program.cs
  Workslip.Domain/
    Workslip.Domain.csproj
  Workslip.Application/
    Workslip.Application.csproj
    Jobs/
    Documents/                  # parkeret/future-scope
  Workslip.Infrastructure/
    Workslip.Infrastructure.csproj
    Repositories/
    Migrations/
  Migrations/
    001_init_job.sql
```

## Current active endpoints

### Health

- `GET /health`

### Jobs

- `POST /api/jobs`
- `GET /api/jobs`
- `GET /api/jobs/{id}`
- `PATCH /api/jobs/{id}`
- `POST /api/jobs/{id}/submit`
- `POST /api/jobs/{id}/approve`
- `POST /api/jobs/{id}/reject`

## Naming rules

Use these names consistently:

| Layer | Name |
|---|---|
| Solution | `Workslip.sln` |
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

## Migrations

SQL migrations live in:

`Migrations/*.sql`

The active MVP migration creates the jobs-oriented tables:

- `Organizations`
- `Users`
- `JobReports`
- `JobControlChecks`
- `JobEvents`

## Future-scope document code

Some generic document classes still exist under `Documents/` and `DocumentEndpoints.cs`, but endpoints are not mapped in `Program.cs`.

Treat that code as parked/future-scope unless the product direction explicitly reopens scanning, OCR, generic document types or PDF/document ingestion.

For now, frontend and backend work should target `/api/jobs` and the jobs data model.
