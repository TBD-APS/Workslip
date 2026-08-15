# Job repository composition

**Status:** Active  
**Tracking:** WOR-474, parent WOR-443

`IJobRepository` is an application persistence port. Assignment authorization and initial-assignment selection are application/domain concerns and must be resolved before persistence is called.

## Current composition

`JobService.CreateAsync` resolves initial assignees through `JobAssignmentPolicy.ResolveInitialAssignments(...)` and passes the resolved IDs to `IJobRepository.CreateAsync`.

Infrastructure composes:

```text
IJobRepository -> BillingAwareJobRepository -> EfJobRepository
```

`AssignmentAwareJobRepository` was removed because it reinterpreted `CreateJobRequest.AssignedUserIds` inside infrastructure after the application service had already resolved the assignment policy. That duplicated business-rule ownership and made DI order semantically significant.

`BillingAwareJobRepository` remains temporarily because approval-time billing snapshot capture is still coupled to the repository transition boundary. Moving that rule requires a separate transaction-aware application boundary and is follow-up technical debt under WOR-443; it is intentionally not mixed into WOR-474.

## Invariants

- Role/assignment policy is owned by `JobAssignmentPolicy` and the application service/validator flow.
- Repositories persist the resolved assignment list they receive; they do not reinterpret actor role or raw assignment intent.
- Tenant filtering, filial validation and assignment validity continue to be enforced by the established application/domain/infrastructure guards.
- Repository decorators may not silently replace application decisions based on request DTO fields.
