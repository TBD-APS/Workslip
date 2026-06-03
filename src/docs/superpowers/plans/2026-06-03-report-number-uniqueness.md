# Report Number Uniqueness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent duplicate job report numbers within the same organization and return a clean frontend-friendly conflict error.

**Architecture:** Enforce uniqueness in SQL Server through EF model configuration for new databases and an idempotent SQL script for existing databases because the repo does not have a usable migration baseline. Keep API behavior pleasant by mapping duplicate report-number conflicts to Ardalis `Result.Conflict("duplicate_report_number")` so endpoints return `409` using the existing `ResultExtensions` pattern.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core, SQL Server, Ardalis.Result, Postman integration collection.

---

### Task 1: Add API Integration Coverage

**Files:**
- Modify: `BE/WorkslipApi/Postman/postman_collection.json`

- [ ] Add a job create request that reuses the existing `{{reportNumber}}` and expects `409 Conflict` with `error = duplicate_report_number`.
- [ ] Place it after the successful create job request so the first job exists before the duplicate request runs.

### Task 2: Enforce DB Uniqueness

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs`
- Create: `BE/WorkslipApi/DatabaseScripts/2026-06-03-add-unique-job-report-number.sql`

- [ ] Add `HasIndex(e => new { e.OrganizationId, e.ReportNumber }).IsUnique().HasDatabaseName("UX_JobReports_Organization_ReportNumber")`.
- [ ] Add an idempotent SQL script that fails clearly if duplicate rows already exist, then creates `UX_JobReports_Organization_ReportNumber` when missing.

### Task 3: Map Duplicate Errors To Conflict

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs`
- Modify: `BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs`

- [ ] Normalize report number by trimming before duplicate checking and persistence.
- [ ] Replace the current thrown `InvalidOperationException` duplicate check with a repository exception type or domain-neutral conflict signal.
- [ ] Catch duplicate report-number failures in `JobService.CreateAsync` and return `Result<JobReportSummaryResponse>.Conflict("duplicate_report_number")`.

### Task 4: Verify

**Files:**
- No source edits.

- [ ] Run `dotnet build "Workslip.Api.csproj" -o "C:\Users\rasmu\AppData\Local\Temp\opencode\workslip-api-build"` from `BE/WorkslipApi`.
- [ ] If the API is running and disposable test data is available, run the Postman duplicate scenario and confirm `409`.
