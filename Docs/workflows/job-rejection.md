---
id: workslip.workflow.job-rejection
product: workslip
type: workflow
status: active
owner: product/workslip
visibility: internal
audience: [agent, developer, operator]
capabilities: [jobs]
last_reviewed: 2026-11-16
review_status: approved
source_refs: [https://github.com/TBD-APS/Workslip/tree/785c49b1b911f76b5e5a8207ac57d90fb5922c84/src/BE/WorkslipApi]
relations: []
code_refs: [src/BE/WorkslipApi/Workslip.Domain/JobStatusTransitionPolicy.cs, src/BE/WorkslipApi/Workslip.Application/Jobs/AuthorizedJobService.cs, src/BE/WorkslipApi/Workslip.Application/Jobs/JobLifecycleService.cs, src/BE/WorkslipApi/Workslip.Application/Jobs/Validators/ChangeJobStatusRequestValidator.cs, src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs, src/BE/WorkslipApi/Workslip.Tests/Jobs/JobRejectionNotificationTests.cs, src/BE/WorkslipApi/Workslip.Tests/Jobs/JobReopenedStabilityTests.cs]
api_refs: [/api/jobs/{id}/status]
linear_refs: [SAS-36]
adr_refs: []
---
# Job rejection and return for correction

**Status:** Active — source-reviewed internal documentation. Deployed, HTTP and browser behavior was not exercised for this change. Approval records independent source review for this implementation pilot; it does not claim human owner sign-off.

## Purpose and actors

A reviewer returns a job for correction while keeping the reason with the job. The backend distinguishes rejection during review from correction after approval. `Admin` and `Superadmin` are reviewer roles; `User` cannot reject, approve or reopen a job. These rules are enforced by [the domain transition policy](../../src/BE/WorkslipApi/Workslip.Domain/JobStatusTransitionPolicy.cs) through [the authorized service](../../src/BE/WorkslipApi/Workslip.Application/Jobs/AuthorizedJobService.cs).

## Preconditions and API

The status command is `POST /api/jobs/{id}/status` in [JobEndpoints](../../src/BE/WorkslipApi/Endpoints/JobEndpoints.cs). It requires an `Idempotency-Key`; a missing key returns HTTP 428. The organization and user come from the current authenticated context. Job lookup is scoped to that organization, and the authorized service applies job access and transition permissions before invoking the lifecycle service.

[Request validation](../../src/BE/WorkslipApi/Workslip.Application/Jobs/Validators/ChangeJobStatusRequestValidator.cs) requires a non-empty `rejectionNote` for both `Rejected` and `Reopened`.

## States and transitions

| Current state | Target | Meaning |
| --- | --- | --- |
| `InReview` | `Rejected` | Reviewer returns the submitted job for correction. |
| `Approved` | `Reopened` | Reviewer returns an approved job for correction. |
| `Rejected` | `InReview` | Corrected job is submitted again, subject to submit-readiness validation. |
| `Reopened` | `InReview` | Reopened job is submitted again, subject to submit-readiness validation. |

The policy rejects `Approved` → `Rejected`, `Rejected` → `Draft` and `Reopened` → `Draft`. Withdrawal to `Draft` exists from `InReview`; it is a separate transition. Same-status commands are allowed by the transition policy, subject to reviewer permissions for reviewer target states.

## Persistence and correction routing

[EfJobRepository](../../src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs) writes status and correction reason together. Entering `InReview` records the submitting actor in `SubmittedByUserId`, preserves the first `SubmittedAt`, and clears the correction reason. A same-status persistence call returns an unchanged transition snapshot.

After `Rejected` or `Reopened` is persisted, [JobLifecycleService](../../src/BE/WorkslipApi/Workslip.Application/Jobs/JobLifecycleService.cs) uses that persisted submitter for correction routing. If the submitter resolves within the job organization, the job is assigned to that person. Routing does not infer the submitter from presentation history. If no valid submitter resolves, current assignees other than the actor are the notification fallback; this fallback does not assign an arbitrary replacement submitter.

## Failure and retry behavior

- A missing organization or actor is unauthorized; an unavailable scoped job is not found. A forbidden role is forbidden, and an invalid state transition is a conflict in the authorized service.
- Submit-readiness validation runs when actually entering `InReview`. It does not block a reviewer decision or same-status retry because reference data has changed since submission.
- Submitter reassignment is required correction work and can fail after the status has persisted. A new same-status command can repair the assignment and queue a recovery notification when routing changes.
- Correction notifications and cache invalidation are best-effort after persistence. A notification queue failure does not undo a persisted correction or turn it into a reported transition failure. A same-status command with already-correct routing does not requeue a notification solely because an earlier notification failed.

An HTTP idempotency replay returns the recorded response; it does not execute the lifecycle again. Do not equate replaying a completed idempotency key with issuing a new same-status command to repair routing.

## Verification and provenance

These checked-in tests are evidence for the documented implementation, not a claim that they were executed for this documentation change:

- [JobStatusTransitionPolicyTests](../../src/BE/WorkslipApi/Workslip.Tests/Jobs/JobStatusTransitionPolicyTests.cs): role/state matrix.
- [JobRejectionNotificationTests](../../src/BE/WorkslipApi/Workslip.Tests/Jobs/JobRejectionNotificationTests.cs): persisted-submitter routing, bounded fallback, notification failure after persistence and same-status routing repair.
- [JobLifecyclePersistenceTests](../../src/BE/WorkslipApi/Workslip.Tests/Jobs/JobLifecyclePersistenceTests.cs): persisted reason and invalid transitions to `Draft`.
- [JobReopenedStabilityTests](../../src/BE/WorkslipApi/Workslip.Tests/Jobs/JobReopenedStabilityTests.cs): reopen, edit and resubmit with reason clearing; its in-memory provider does not prove SQL constraints.
- [The correction Postman collection](../../src/BE/WorkslipApi/Postman/job_correction.postman_collection.json) defines an HTTP correction scenario. No Postman target was exercised for this page.

The source baseline is Workslip commit `785c49b1b911f76b5e5a8207ac57d90fb5922c84`. The page contains no customer records, job instances, rejection-note values or source-file bodies. Code references are citations for human review; they do not authorize source ingestion.
