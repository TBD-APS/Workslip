# Worksheet Entry Page (rbj-53-app-page-4-arbejdsseddelrapport)

**Branch:** `rbj-53-app-page-4-arbejdsseddelrapport` (already exists)
**Date:** 2026-06-04
**Status:** Awaiting review

## Problem

The technician (montør) has no in-app page to review and supplement the digital worksheet/report on a job. The current `JobDetail` flow ends with a placeholder "Bilag" step. Worksheet data is captured by the backend but never displayed in the app. Before final submission, the technician needs a single page that shows the existing worksheets, lets them add new ones, and lets them remove ones they no longer need. The whole page should drive off the same `JobReportSummaryViewModel` so the FE uses one model throughout.

## Solution

A new "Arbejdssedler" step (step 4 of the edit flow) that:

- Renders only the existing worksheets list and an inline "Tilføj arbejdsseddel" form. No summary section.
- Reads initial state from `JobReportSummaryViewModel` (loaded once by `useJobDetails` via `GET /api/jobs/{id}`).
- Writes via the worksheet upsert and delete endpoints, which now return the updated `JobReportSummaryViewModel` so the FE refreshes in one call.

The `GET /api/worksheets/jobs/{jobId}` endpoint is removed. All worksheet state lives in the summary.

## Scope

In scope:
- New `Arbejdssedler` step in `JobDetail` (replaces placeholder `Bilag`).
- BE: embed `WorksheetViewModel` list in `JobReportSummaryViewModel` and `JobReportSummaryResponse`.
- BE: change worksheet `POST` and `DELETE` to return `JobReportSummaryViewModel`.
- BE: remove `GET /api/worksheets/jobs/{jobId}`.
- New `WorksheetViewModel` in `Workslip.Api/ViewModels` with `JobViewModelBuilder.ToWorksheet` mapper.
  - **Decision (revised):** Reuse `WorksheetResponse` directly in `JobReportSummaryViewModel` to follow the existing pattern (the view model already reuses `JobLinkInfoResponse`, `AssignedUserResponse`, etc.). No new view model type is introduced.
- Update `postman_collection.json` for the changed worksheet endpoints and remove the GET.

Out of scope (deferred):
- PDF preview / print layout.
- Email sending.
- Admin approval.
- Submit / attestation flow (Draft → Submitted).
- Validation feedback UI for missing items (user to specify later).
- Inline edit of a worksheet (UI only exposes create + delete; "edit" is delete + recreate).
- Sagsresumé (job summary) section on the new page.

## Flow

```
Sagsdetaljer (0) → Kategorier (1) → Kontrolpunkter (2) → Arbejdssedler (3)
```

`JOB_STEPS` becomes:

```ts
[
  { icon: Building2,         label: 'Sagsdetaljer' },
  { icon: FileText,          label: 'Kategorier' },
  { icon: ClipboardList,     label: 'Kontrolpunkter' },
  { icon: FileSpreadsheet,   label: 'Arbejdssedler' },
]
```

The new step's "Færdig" button navigates back to `/app` (no status change). "Tilbage" goes to step 2.

## Backend

### Domain / Application

`Workslip.Application/Jobs/JobContracts.cs`

Add `IReadOnlyList<WorksheetResponse> Worksheets` to `JobReportSummaryResponse`.

`Workslip.Application/Jobs/JobService.cs`

When building `JobReportSummaryResponse`, also load worksheets via the existing `IWorksheetRepository.GetGroupedByJobAsync(jobId)` and flatten the user-grouped rows into a single `IReadOnlyList<WorksheetResponse>`.

`Workslip.Application/Worksheets/WorksheetService.cs`

- Inject `IJobService`. (No cycle: `IJobService` does not depend on `IWorksheetService`.)
- `UpsertAsync(UpsertWorksheetRequest, CancellationToken)` returns `Task<Result<JobReportSummaryResponse>>` — calls `_repository.UpsertAsync`, then awaits `_jobService.GetSingleJobAsync(jobId, ct)` and returns that result on success. On `Result.Invalid` from validation, return the validation result directly (no need to load summary).
- `DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken)` returns `Task<Result<JobReportSummaryResponse>>` — calls `_repository.DeleteAsync(worksheetId, jobId, ct)`, then loads the summary via `_jobService.GetSingleJobAsync` and returns that.
- Keep existing FluentValidation mapping (`Result.Invalid(...)`); logs only `jobId` and field names (PII-sparse).

`Workslip.Application/Worksheets/WorksheetContracts.cs`

- `IWorksheetService.UpsertAsync` return type → `Task<Result<JobReportSummaryResponse>>`.
- `IWorksheetService.DeleteAsync` return type → `Task<Result<JobReportSummaryResponse>>`.
- Drop `IWorksheetService.ListByJobAsync` (no longer needed).

### API

`Workslip.Api/ViewModels/JobViewModels.cs`

`JobReportSummaryViewModel` gets one new field, reusing the existing `WorksheetResponse` from the Application layer (consistent with how `JobLinkInfoResponse`, `AssignedUserResponse`, etc. are reused in the view model today):

```csharp
public sealed record JobReportSummaryViewModel(
    Guid Id,
    Guid OrganizationId,
    string? ReportNumber,
    JobStatus Status,
    CustomerInfo Customer,
    JobReportSummaryWorkResponse Work,
    JobReportSummaryObservationResponse Observations,
    IReadOnlyList<JobLinkInfoResponse> Links,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    bool SoftDeleted,
    IReadOnlyList<WorksheetResponse> Worksheets);   // NEW
```

Update `JobViewModelBuilder.ToSummary` to accept and pass `worksheets`. No new mapping methods are required — `WorksheetResponse` is passed through as-is.

`Workslip.Api/Endpoints/WorksheetEndpoints.cs`

- Remove `GET /api/worksheets/jobs/{jobId}`.
- `POST /api/worksheets/jobs/{jobId}` → calls `service.UpsertAsync(...)` and maps via `ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary)`. The mapper is applied to `Result<JobReportSummaryResponse>` → `JobReportSummaryViewModel`.
- `DELETE /api/worksheets/{worksheetId}/jobs/{jobId}` → calls `service.DeleteAsync(...)` and applies the same mapper.

### Error handling

Reuse the existing `WorksheetService` patterns:

| Status | Mapping |
|---|---|
| Validation failure | `Result.Invalid(errors)` → 400 with field-level details |
| Conflict (e.g. duplicate upsert business rule) | `Result.Conflict(...)` → 409 |
| Auth missing | `Result.Unauthorized()` → 401 |
| Unexpected | `Result.Error(...)` → 500 |

Log messages: include `jobId` and field names only. Never log customer name, email, phone, or report number.

## Frontend

### Step component

`src/FE/src/features/jobs/components/steps/JobWorksheetsStep.tsx` (new)

Two sections, single column:

1. **Eksisterende arbejdssedler** — table-style list of `worksheets` from the loaded `JobReportSummaryViewModel`. Columns: `Dato`, `Montør` (display name from `useGetApiUsers`), `Timer`, `Overnattet` (ja/nej). Each row has a `Slet` button → `useDeleteApiWorksheetsWorksheetIdJobsJobId` with a `confirm()` prompt. Empty state: "Ingen arbejdssedler endnu."
2. **Tilføj arbejdsseddel** — inline form. Fields: `Dato` (defaults to today, `type="date"`), `Timer` (number, step 0.5, min 0), `Overnattet` (checkbox), `Montør` (dropdown of `useGetApiUsers`; defaults to the current user from `useAuth` — admins can change). Submit button calls `usePostApiWorksheetsJobsJobId`.

Both mutations update the `getApiJobsId` cache with `response.data` on success so the list and form stay in sync without a refetch.

### Hook updates

`src/FE/src/features/jobs/hooks/useJobDetails.ts`

- Expose `worksheets: WorksheetResponse[]` derived from `job?.worksheets ?? []`.
- Add `upsertWorksheet(input: UpsertWorksheetInput)` that calls `usePostApiWorksheetsJobsJobId`, then on success writes the response into the `getApiJobsId` cache and toasts `'Arbejdssedlen er gemt'`. On 4xx/5xx, toast the error and refetch the job to keep UI in sync.
- Add `deleteWorksheet(worksheetId)` that calls `useDeleteApiWorksheetsWorksheetIdJobsJobId`, then on success writes the response into the `getApiJobsId` cache and toasts `'Arbejdssedlen er slettet'`.
- Keep the existing draft/autosave machinery on the customer/observations/work steps untouched.

`src/FE/src/features/jobs/components/steps/JobStepNavigation.tsx`

Replace the `Bilag` entry (index 3) with `Arbejdssedler` (icon `FileSpreadsheet` from `lucide-react`).

`src/FE/src/features/jobs/components/JobDetails.tsx`

- The `currentStep === 3` branch renders `<JobWorksheetsStep details={details} />` instead of `<JobAttachmentsStep />`.
- `isLastStep` is still `details.currentStep === JOB_STEPS.length - 1` (now index 3).
- "Færdig" still calls `onDone` → `/app`; no status change.

`src/FE/src/features/jobs/components/steps/JobAttachmentsStep.tsx`

Delete this file.

`src/FE/src/api/generated/**`

Regenerate via orval after BE changes (run the project's orval script). Expected: `useGetApiWorksheetsJobsJobId` removed; `usePostApiWorksheetsJobsJobId` and `useDeleteApiWorksheetsWorksheetIdJobsJobId` now return `JobReportSummaryViewModel`.

### Error handling

- 400 (validation) → inline error under the offending field; toast on top of that with a generic "Tjek felterne" message.
- 409 → toast with the backend `error` code mapped to a Danish message; reuse the `getSaveErrorMessage` pattern from `useJobDetails`.
- Network / 5xx → toast `'Kunne ikke gemme arbejdssedlen'` / `'Kunne ikke slette arbejdssedlen'`; the affected row stays in place until next successful refresh.

## Files Touched

### Backend
- `Workslip.Application/Jobs/JobContracts.cs` — add `Worksheets` to `JobReportSummaryResponse`.
- `Workslip.Application/Jobs/JobService.cs` — load worksheets when building summary.
- `Workslip.Application/Worksheets/WorksheetContracts.cs` — change service return types, drop `ListByJobAsync`.
- `Workslip.Application/Worksheets/WorksheetService.cs` — return summary from upsert/delete.
- `Workslip.Api/ViewModels/JobViewModels.cs` — add `Worksheets` to `JobReportSummaryViewModel`; update `ToSummary` to accept and pass `worksheets` (no new view model type).
- `Workslip.Api/Endpoints/WorksheetEndpoints.cs` — remove GET, change POST/DELETE to return `JobReportSummaryViewModel`.

### Frontend
- `src/FE/src/features/jobs/components/steps/JobWorksheetsStep.tsx` — new.
- `src/FE/src/features/jobs/components/steps/JobAttachmentsStep.tsx` — delete.
- `src/FE/src/features/jobs/hooks/useJobDetails.ts` — expose `worksheets`, add `upsertWorksheet` / `deleteWorksheet`.
- `src/FE/src/features/jobs/components/JobDetails.tsx` — swap step 3 component.
- `src/FE/src/features/jobs/components/steps/JobStepNavigation.tsx` — rename step 3 to `Arbejdssedler`.
- `src/FE/src/api/generated/**` — regenerated.

### Verification
- `src/BE/WorkslipApi/Postman/postman_collection.json` — update worksheet section: remove GET entry, change POST and DELETE response shape assertions to the new `JobReportSummaryViewModel` (assert `status`, `id`, `worksheets` array, and the new worksheet present), drop `worksheetJobId` and `worksheetId` collection variables if no longer used elsewhere.

## Verification

Manual smoke flow on a running stack:

Run twice — once on a draft job, once on a complete (non-draft) job — to confirm the step is reachable and behaves the same in both cases:

1. Open a job (draft and complete). Step 4 "Arbejdssedler" is reachable from the step indicators.
2. Empty list shows the empty state. Form is ready with today's date and the current user pre-filled.
3. Submit a new worksheet (4 hours, not slept). Row appears in the list with the correct values; toast confirms save.
4. Refresh the page; the worksheet is still there (persisted).
5. Delete the worksheet; row disappears, toast confirms; refresh confirms it's gone.
6. Validation: submit the form with `Timer = 0` → inline error under the field, no network call fires.
7. Network: stop the API and submit → toast `Kunne ikke gemme arbejdssedlen`.

Postman collection:

- Run the collection with `run-integration-tests.sh` (or `newman run`). All non-worksheet tests still pass.
- The "Worksheets" folder:
  - `GET /api/worksheets/jobs/{jobId}` is removed.
  - `POST /api/worksheets/jobs/{jobId}` asserts 200, response is an object with `id`, `worksheets` array, and the new worksheet is in `worksheets`.
  - `DELETE /api/worksheets/{worksheetId}/jobs/{jobId}` asserts 200 and the response `worksheets` array does not include the deleted worksheet.

## Acceptance criteria

- Report summary data is reflected in the worksheets list after each upsert/delete (single source of truth: `JobReportSummaryViewModel`).
- The technician can add a worksheet and remove a worksheet from the new step.
- Error states are user-friendly (toasts) and BE logs are PII-sparse (only `jobId` and field names).
- 4-step flow: `Sagsdetaljer → Kategorier → Kontrolpunkter → Arbejdssedler`.
- Postman collection is updated and the `run-integration-tests.sh` script passes.
