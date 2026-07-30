# Workslip backend/API instructions

Read the root `AGENTS.md`, `Docs/agents/OPERATING_CONTRACT.md`, and `Docs/agents/VALIDATION.md` before changing backend code.

## Scope

These rules apply to `src/BE/WorkslipApi/`, including API host, application, domain, infrastructure, persistence, and backend tests.

## Layer boundaries

- Keep endpoints thin.
- Put business rules and workflow decisions in the application or domain layer.
- Keep EF Core, SQL, Graph, email, storage, and other integration details in infrastructure.
- Do not expose persistence models directly as API contracts.
- Do not introduce wrappers or abstractions without a concrete current need.
- Reuse established repositories, services, validators, and result mapping.

## Ardalis.Result contract

Application services return `Result<T>` or `Result`.

- Never create custom result wrappers.
- Map endpoint results with `ResultExtensions.ToHttpResult`.
- Do not duplicate result-to-HTTP mapping inside endpoints.
- Use endpoint-owned mappers only for presentation concerns such as view models or JWT responses.
- Keep success responses consistent with the existing `200 OK` convention.
- Do not add redundant response-cache calls when global middleware already enforces the behavior.

Example:

```csharp
var result = await service.UpdateAsync(id, request, cancellationToken);
return ResultExtensions.ToHttpResult(result, ViewModelBuilder.ToViewModel);
```

## Authorization and tenant isolation

For every affected operation, verify:

- the endpoint policy is correct;
- authorization is enforced server-side;
- entity identifiers cannot escape the authenticated organization;
- repository filters and joins retain tenant boundaries;
- Superadmin behavior is explicit and narrowly scoped;
- role changes and revocation take effect correctly;
- logs do not expose tokens, personal data, or sensitive integration payloads.

Do not implement tenant selection through untrusted client headers or frontend state. Do not add repository-wide Superadmin bypasses.

## EF Core and database behavior

Review:

- query translation and tracking behavior;
- bounded results, pagination, indexes, and N+1 risks;
- transaction boundaries and rollback;
- SQL Server retry execution strategies around explicit transactions;
- optimistic concurrency and lost updates;
- idempotency and replay;
- deletion behavior and orphaned records;
- existing production data before adding constraints;
- cancellation propagation and command timeouts;
- startup schema behavior and concurrent API starts.

Use a relational database test for behavior that depends on SQL translation, constraints, transactions, or concurrency. The EF Core in-memory provider is not sufficient evidence for those concerns.

## External integrations and partial failure

For Microsoft Graph, Entra, email, storage, or other integrations, define behavior when:

- the external call succeeds and SQL persistence fails;
- SQL succeeds and the external call fails;
- compensation fails;
- the request is retried;
- the external system times out or returns a transient error;
- two requests perform the same provisioning concurrently.

Use compensation, retries, idempotency, and transactions only where justified by the actual failure boundary. Log identifiers needed for support without logging secrets.

## Background services

Review hosted-service changes for:

- unhandled exceptions that stop the API host;
- cancellation and graceful shutdown;
- retry and backoff behavior;
- duplicate work after restart;
- transaction and execution-strategy compatibility;
- poison work items and partial processing;
- sensitive-data logging.

Do not configure the host to ignore failing background services merely to hide an unhandled exception. Correct the failure and define restart behavior.

## API contracts and generated artifacts

- Endpoint source and runtime OpenAPI are the contract source.
- Update maintained API documentation with endpoint, auth, or response changes.
- Do not hand-edit generated clients or generated documentation.
- Regenerate the frontend client and Postman material through the established process.
- Preserve backward compatibility unless the product owner explicitly accepts a break.

## Backend tests

Add focused regression tests for meaningful risk:

- business rules and branching workflows;
- authorization and tenant isolation;
- transactions, rollback, retries, and idempotency;
- concurrency and state transitions;
- relational constraints and SQL behavior;
- integration boundaries;
- hosted-service lifecycle and failure handling;
- verified bugs and critical edge cases.

Do not add tests for trivial mappings, getters, framework behavior, or CRUD pass-through solely to increase coverage.

## Required validation

At minimum for backend runtime changes:

- restore dependencies;
- compile the solution in Release mode;
- run focused backend tests;
- run relational tests when database behavior is affected;
- exercise changed HTTP endpoints when API behavior or authorization is affected;
- verify OpenAPI/client/Postman consistency when contracts change.

When the feature also changes frontend behavior, the frontend Playwright requirement applies in addition to backend validation.
