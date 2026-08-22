# Job repository composition

**Status:** Active  
**Tracking:** WOR-112, WOR-474, WOR-550, parent WOR-443

`IJobRepository` is an application persistence port. Assignment authorization and initial-assignment selection are application/domain concerns and must be resolved before persistence is called.

## Current composition

`JobService.CreateAsync` resolves initial assignees through `JobAssignmentPolicy.ResolveInitialAssignments(...)` and passes the resolved IDs to `IJobRepository.CreateAsync`.

Infrastructure composes:

```text
IJobRepository -> BillingAwareJobRepository -> EfJobRepository
```

`AssignmentAwareJobRepository` was removed because it reinterpreted `CreateJobRequest.AssignedUserIds` inside infrastructure after the application service had already resolved the assignment policy. That duplicated business-rule ownership and made DI order semantically significant.

`BillingAwareJobRepository` remains temporarily because approval-time billing snapshot capture is still coupled to the repository transition boundary. Moving that rule requires a separate transaction-aware application boundary and is follow-up technical debt under WOR-443; it is intentionally not mixed into WOR-474.

## Canonical job lifecycle

WOR-112 locks the current product lifecycle to the five persisted `JobStatus` values. Inventory, demo, analytics and onboarding code must consume this model rather than invent parallel statuses.

```text
Draft
  | submit (submit-ready)
  v
InReview -------------------- approve --------------------> Approved
  |                                                         |
  | reject(reason)                                          | reopen(reason)
  v                                                         v
Rejected ----------------- resubmit ---------------------> InReview
                                                            ^
                                                            |
Reopened ----------------- resubmit ------------------------+
```

`Rejected` and `Reopened` are correction states, not terminal states and not aliases for `Draft`:

- `Rejected` means a reviewer rejected the submitted job and returned it for correction. The persisted submitter is preferred when the job is reassigned. A corrected job returns directly to `InReview`.
- `Reopened` means an already approved job was explicitly reopened by an Admin/Superadmin for correction. It remains distinct from a never-submitted draft and returns directly to `InReview` after correction.
- `Approved` is the accepted review outcome. Reopening is the only supported transition out of it.
- `Draft` is the initial editable state. There is no supported transition back to `Draft` after submission.
- `InReview` is the submitted/review state. Submit-readiness is checked when entering this state, not when a reviewer later accepts or rejects the already-submitted snapshot.

There is deliberately **no `Cancelled` job status**. Administrative delete/restore is a separate lifecycle exposed through separate endpoints and repository operations; it must not be represented as an undocumented status transition.

### Transition matrix

| Current | Target | Allowed role | Submit-ready validation | Reason | Key lifecycle effects |
| --- | --- | --- | --- | --- | --- |
| `Draft` | `InReview` | User, Admin, Superadmin | Required | No | Persist submitter; queue review notifications |
| `Rejected` | `InReview` | User, Admin, Superadmin | Required | No | Persist current resubmitter; clear correction reason; queue review notifications |
| `Reopened` | `InReview` | User, Admin, Superadmin | Required | No | Persist current resubmitter; clear correction reason; queue review notifications |
| `InReview` | `Approved` | Admin, Superadmin | **Not rerun** | No | Queue completion notifications; mark completed view |
| `InReview` | `Rejected` | Admin, Superadmin | **Not rerun** | Required | Persist rejection reason; prefer persisted submitter for reassignment; queue denial notification |
| `Approved` | `Reopened` | Admin, Superadmin | **Not rerun** | Required | Persist reopen/correction reason; make job editable again |
| same status | same status | Role authorized for that target | No new lifecycle work | Existing request rules still apply | Idempotent: no repeated lifecycle side effects |

All other source/target pairs are conflicts. A User targeting `Approved`, `Rejected` or `Reopened` is forbidden. Auditor/unknown roles cannot mutate lifecycle state. Inaccessible jobs remain not-found so transition handling does not weaken tenant/assignment scoping.

The persisted `RejectionNote` field is a legacy physical name. At the lifecycle boundary it is the current **correction reason** for both `Rejected` and `Reopened`; entering `InReview` or `Approved` clears it. A schema rename is not required to express the product rule and is intentionally not coupled to WOR-112.

## Status lifecycle ownership

`AuthorizedJobService` remains the product-facing transition authorization boundary. It evaluates `JobStatusTransitionPolicy` before delegating and therefore continues to own role-aware transition permission, scoped job visibility and the conflict/forbidden result semantics established by WOR-217.

`JobLifecycleService` owns the application orchestration after that authorization boundary: submit-readiness validation **when entering `InReview`**, persistence transition, duplicate-transition handling, rejection reassignment, review/rejection/completion notification routing, completed-view bookkeeping and transition cache invalidation. Reviewer decisions (`Approved`, `Rejected`, `Reopened`) do not rerun mutable submission prerequisites after the job has already entered review. `JobService.ChangeStatusAsync` remains only the compatibility entry point that delegates to this owner; public endpoints and `IJobService` are unchanged.

The generated dependency map shows that the Jobs module still has the same three outbound module edges and the same fan-in/fan-out coupling score. File-level outbound references rise because the lifecycle owner now names the same existing Auth, Worksheets and Notifications contracts directly instead of hiding those responsibilities in `JobService`; no new module dependency is introduced. These contracts are kept explicit rather than replaced with generic wrappers solely to improve the metric. Notification durability and transaction semantics are intentionally unchanged.

## Transaction, concurrency and idempotency

The lifecycle has two complementary idempotency/concurrency guards:

1. `POST /api/jobs/{id}/status` requires an `Idempotency-Key`. The endpoint reserves the operation under organization, actor and job scope, replays a completed response for the same request, and aborts the reservation when the application result is unsuccessful.
2. `EfJobRepository.TransitionAsync` runs the persisted status change in a **serializable database transaction**. A same-status transition returns `Changed = false`; `JobLifecycleService` then returns the current summary without repeating cache/notification lifecycle side effects.

`JobStatusTransitionInterceptor` revalidates the original and target status during `SaveChanges`, inside the repository transaction and before the audit interceptor. A stale application-layer read therefore cannot commit a now-invalid source/target pair; it becomes the established `invalid_job_status_transition` conflict instead.

The database transition commits before cache invalidation and notification queueing. That existing partial-failure boundary is explicit: WOR-112 does not pretend status persistence and external notification delivery are one atomic transaction, and it does not add a distributed transaction or outbox opportunistically. Notification durability remains separate follow-up work.

## Invariants

- Role/assignment policy is owned by `JobAssignmentPolicy` and the application service/validator flow.
- Status transition authorization remains in `AuthorizedJobService`/`JobStatusTransitionPolicy`; lifecycle orchestration must not bypass it.
- Submit-readiness validation belongs only to transitions into `InReview`.
- Reviewer decisions cannot be blocked solely because reference data or submission-validation rules changed after submission.
- Rejected and reopened jobs remain editable correction states and resubmit directly to `InReview`.
- Reopen/rejection reasons survive while the job is in the corresponding correction state and are cleared when review resumes.
- Repositories persist the resolved assignment list they receive; they do not reinterpret actor role or raw assignment intent.
- Tenant filtering, filial validation and assignment validity continue to be enforced by the established application/domain/infrastructure guards.
- Duplicate status transitions remain idempotent and do not repeat lifecycle side effects.
- Rejection continues to prefer the persisted submitter and falls back to current assignees for legacy data.
- Notification queueing remains post-transition work with the pre-existing partial-failure semantics; this boundary does not introduce an outbox or expand database transactions around notifications/cache operations.
- Deletion/restore is not a `JobStatus` transition and must not be surfaced as `Cancelled` without a separate domain decision.
- Repository decorators may not silently replace application decisions based on request DTO fields.
