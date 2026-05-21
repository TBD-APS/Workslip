# Jobs API + MVP Docs Cleanup Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Align backend and documentation with the actual MVP: jobs-based 4V05 case/report flow, no AI/OCR in MVP, and no `old workslip route` terminology in API contracts.

**Architecture:** Keep the strong demo product foundation: installer/PWA works with jobs/cases and 4V05 controls; backoffice reviews submitted jobs. For MVP, treat manual job data entry/import as source of truth. Park AI/OCR/scanning/document-intelligence as future roadmap, not active MVP architecture.

**Tech Stack:** .NET 10 Minimal API, SQL Server/LocalDB, Dapper, Onion-style projects, Obsidian Markdown docs.

---

## Decisions

1. Public API terminology is `jobs`, not `workslips`.
2. MVP excludes AI, OCR, LLM extraction, Document Intelligence, confidence scores, and AI review scoring.
3. MVP keeps KLS-relevant value: 4V05 fields, control checks, status workflow, digital attestation, audit trail, PDF/export later if required.
4. Backend source of truth for MVP is the Jobs API, not the disabled generic Document API.
5. Docs should distinguish `active MVP` from `archive/future ideas`.

---

## Task 1: Add backend contract tests for Jobs API naming

**Objective:** Lock the public API to `/api/jobs` and prevent `old workslip route` from returning as active documentation/code.

**Files:**
- Create: `src/BE/WorkslipApi/Workslip.Tests/ApiNamingTests.cs`
- Modify: `src/BE/WorkslipApi/Workslip.sln`

**Steps:**
1. Create a test project if none exists.
2. Add a test that scans source files excluding `bin/` and `obj/`.
3. Fail if active `.cs` files contain `old workslip route`, `JobReports`, or `JobControlChecks`.
4. Allow `Workslip.Api.csproj` temporarily as assembly/project name unless renamed in a separate step.
5. Run test and verify it fails before cleanup.
6. Rename code/table references until test passes.

**Verification:**
`/mnt/c/Program Files/dotnet/dotnet.exe test Workslip.sln`

---

## Task 2: Fix table-name mismatch in Dapper job repository

**Objective:** Make repository SQL match `Migrations/001_init_job.sql`.

**Files:**
- Modify: `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/DapperJobRepository.cs`
- Consider rename: `DapperJobRepository.cs` -> `DapperJobRepository.cs`

**Required changes:**
- `dbo.JobReports` -> `dbo.JobReports`
- `dbo.JobControlChecks` -> `dbo.JobControlChecks`
- Confirm every query uses the same table names.

**Important:** Current migration schema requires `CustomerId`, but `CreateJobRequest` uses customer fields directly. Either update schema to match request or update request to use `CustomerId`. Do not silently hack around it.

**Verification:**
- Build succeeds.
- Repository SQL strings no longer reference `JobReports` or `JobControlChecks`.

---

## Task 3: Resolve Customer model mismatch

**Objective:** Decide and implement the MVP customer storage shape.

**Preferred MVP approach:** Store customer snapshot fields directly on `JobReports` for now because the demo has job/customer display and does not require full CRM normalization yet.

**Files:**
- Modify: `src/BE/WorkslipApi/Migrations/001_init_job.sql`
- Modify: `src/BE/WorkslipApi/Workslip.Infrastructure/Models/JobReportRow.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/DapperJobRepository.cs`

**Required schema alignment:**
`JobReports` should include fields used by repository/contracts:
- `CustomerName`
- `CustomerAddress`
- `ContactPerson`
- `Phone`

Either remove `CustomerId not null` for MVP or make it nullable/future.

**Verification:**
Build + migration/repository contract test.

---

## Task 4: Rename repository and README terminology from workslip to job

**Objective:** Make code and API docs consistently say Jobs API.

**Files:**
- Rename: `DapperJobRepository.cs` -> `DapperJobRepository.cs`
- Modify: class names/namespaces if needed
- Modify: `src/BE/WorkslipApi/README.md`

**README changes:**
- Title: `Jobs API`
- Remove `old workslip route` endpoint list.
- Document active endpoints under `/api/jobs`.
- Remove generic document/report endpoints from active list unless they are explicitly marked as parked/future.
- Use `ConnectionStrings:JobDB` or `Sql:ConnectionString` consistently.

**Verification:**
- Build succeeds.
- Source search for `old workslip route` returns no active source/docs matches.

---

## Task 5: Park generic Document API for post-MVP

**Objective:** Avoid building two backend models in parallel.

**Files:**
- Modify: `src/BE/WorkslipApi/README.md`
- Optional move: `Workslip.Application/Documents`, `DocumentApi.Endpoints/DocumentEndpoints.cs`, and `DapperDocumentRepository.cs` into a clearly marked future area only if compile remains clean.

**Rule:** Do not delete useful code yet unless asked. Mark it as post-MVP/future so it does not define current architecture.

**Verification:**
Docs state that MVP uses Jobs API; generic document/OCR model is not active MVP.

---

## Task 6: Rewrite active project docs for MVP without AI/OCR

**Objective:** Make docs reflect what is being built now.

**Files to keep/rewrite:**
- `Docs/10-Projects/Workslip/README.md`
- `Docs/10-Projects/Workslip/Start Her.md`
- `Docs/10-Projects/Workslip/09 Q-kontrol - Digital Workslip Procespakke.md`
- `Docs/10-Projects/Workslip/09 Q-kontrol - Feltmapping 4V05 til Workslip.md`
- `Docs/10-Projects/Workslip/10 - Login og Digital Attestering.md`
- `Docs/10-Projects/Workslip/KLS Kravregister.md`

**Rewrite direction:**
- MVP is digital jobs/4V05 documentation.
- Installer creates/completes jobs.
- Backoffice reviews jobs by status.
- Digital attestation/audit trail are key.
- AI/OCR removed from active promises.

**Verification:**
Search active docs for `AI|OCR|LLM|Document Intelligence|gennemgangsscore|confidence` and ensure only archive/future docs contain them.

---

## Task 7: Archive AI/OCR-heavy docs

**Objective:** Preserve old thinking without polluting current MVP scope.

**Suggested archive folder:**
`Docs/10-Projects/Workslip/_archive-ai-ocr/`

**Likely archive/move:**
- `11 - Scannet Papirrapport og AI-gennemgang.md`
- `Dokumenttyper - Digitaliseringskatalog.md`
- `Mail til KLS Auditør.md`
- `KLS spørgsmål og afklaring.md` if it remains AI/OCR-heavy
- `Produkt - Tilbud og Kravgrundlag.md` if rewritten version replaces it

**Verification:**
Active folder root should no longer read like AI/OCR is MVP.

---

## Task 8: Update infrastructure docs/scope

**Objective:** Stop MVP docs from implying Document Intelligence/Logic App is needed.

**Files:**
- `src/BE/infrastructure/README.md`
- Maybe `Docs/10-Projects/Workslip/README.md`

**Changes:**
- Mark Document Intelligence / Logic App OCR pipeline as future/disabled.
- MVP infrastructure should prioritize API + database + hosting + observability + backup/export.

---

## Task 9: Final verification

**Commands:**
```bash
cd /mnt/c/Workslip/src/BE/WorkslipApi
'/mnt/c/Program Files/dotnet/dotnet.exe' build Workslip.sln --no-restore
```

**Search checks:**
- No active API docs mention `old workslip route`.
- No active source SQL references `JobReports` or `JobControlChecks`.
- Active docs do not present AI/OCR as MVP.

---

## Open naming question

Product/UI can still be branded `Workslip` if desired. The requested backend/API terminology is `jobs`. Decide separately whether product docs should say `Workslip`, `QRapport`, or a customer-facing name.
