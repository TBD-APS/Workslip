# Job repository composition

**Status:** Active  
**Tracking:** WOR-474, WOR-550, parent WOR-443

`IJobRepository` is an application persistence port. Assignment authorization and initial-assignment selection are application/domain concerns and must be resolved before persistence is called.

## Current composition

`JobService.CreateAsync` resolves initial assignees through `JobAssignmentPolicy.ResolveInitialAssignments(...)` and passes the resolved IDs to `IJobRepository.CreateAsync`.

Infrastructure composes:

```text
IJobRepository -> BillingAwareJobRepository -> EfJobRepository
```

`AssignmentAwareJobRepository` was removed because it reinterpreted `CreateJobRequest.AssignedUserIds` inside infrastructure after the application service had already resolved the assignment policy. That duplicated business-rule ownership and made DI order semantically significant.

`BillingAwareJobRepository` remains temporarily because approval-time billing snapshot capture is still coupled to the repository transition boundary. Moving that rule requires a separate transaction-aware application boundary and is follow-up technical debt under WOR-443; it is intentionally not mixed into WOR-474.

## Status lifecycle ownership

`AuthorizedJobService` remains the product-facing transition authorization boundary. It evaluates `JobStatusTransitionPolicy` before delegating and therefore continues to own role-aware transition permission, scoped job visibility and the conflict/forbidden result semantics established by WOR-217.

`JobLifecycleService` owns the application orchestration after that authorization boundary: submit-readiness validation, persistence transition, duplicate-transition handling, rejection reassignment, review/rejection/completion notification routing, completed-view bookkeeping and transition cache invalidation. `JobService.ChangeStatusAsync` is now only the compatibility entry point that delegates to this owner; public endpoints and `IJobService` are unchanged.

The generated dependency map shows that the Jobs module still has the same three outbound module edges and the same fan-in/fan-out coupling score. File-level outbound references rise because the lifecycle owner now names the same existing Auth, Worksheets and Notifications contracts directly instead of hiding those responsibilities in `JobService`; no new module dependency is introduced. These contracts are kept explicit rather than replaced with generic wrappers solely to improve the metric. Notification durability and transaction semantics are intentionally unchanged.

## Invariants

- Role/assignment policy is owned by `JobAssignmentPolicy` and the application service/validator flow.
- Status transition authorization remains in `AuthorizedJobService`/`JobStatusTransitionPolicy`; lifecycle orchestration must not bypass it.
- Repositories persist the resolved assignment list they receive; they do not reinterpret actor role or raw assignment intent.
- Tenant filtering, filial validation and assignment validity continue to be enforced by the established application/domain/infrastructure guards.
- Duplicate status transitions remain idempotent and do not repeat lifecycle side effects.
- Rejection continues to prefer the persisted submitter and falls back to current assignees for legacy data.
- Notification queueing remains post-transition work with the pre-existing partial-failure semantics; this boundary does not introduce an outbox or expand database transactions around notifications/cache operations.
- Repository decorators may not silently replace application decisions based on request DTO fields.
