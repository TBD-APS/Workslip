# Workslip agent operating contract

The product owner defines functionality, business constraints, priority, compatibility requirements, and explicit scope. The implementation agent owns repository inspection, technical design, maintainability, security, scalability, implementation, testing, validation, documentation, branch hygiene, and pull-request quality.

The product owner should not need to supervise implementation details. Interrupt them only for material product decisions, irreversible data semantics, commercial trade-offs, or a genuine conflict between stated requirements.

# Repository truth and lookup order

Before answering questions or changing this repository, inspect available sources instead of guessing.

## Source of truth

1. Current repository code, applicable `AGENTS.md` files, executable tests, database mappings, and runtime configuration.
2. Active ADRs and maintained architecture documentation.
3. Linear for issue scope, priority, acceptance criteria, ownership, and status.
4. Current repomix output and generated contracts where applicable.
5. Historical plans and specifications as context only.

OpenAPI is the API contract source when it matches running endpoint registrations. Postman is verification material, not a competing contract.

## Required lookup order

1. Inspect the current branch, worktree, base branch, and changed files.
2. Read the relevant Linear issue and applicable `AGENTS.md`, ADRs, architecture docs, and README files.
3. Use repomix, kioki, and `rg` to inspect existing implementation patterns.
4. Use database/schema tools before reasoning about tables, columns, EF mappings, migrations, seed data, or SQL behavior.
5. Use primary package documentation before changing EF Core, ASP.NET Core, Microsoft Graph, authentication, or frontend-library behavior.
6. Use browser/testing tools such as Playwright when the task involves UI behavior, routing, forms, authentication flows, or end-to-end validation.

If a required tool is unavailable, state that explicitly and continue with best-effort reasoning. Do not silently replace validation with assumptions.

# Repository state gate

Before editing:

- confirm the current branch and base branch;
- inspect branch divergence and changed files;
- check for uncommitted changes when a local worktree is available;
- search for conflict markers, generated credentials, secrets, and accidental environment values;
- verify the branch contains only the intended Linear issue;
- identify applicable documentation and generated artifacts.

Stop implementation and repair or report the state when any of the following is found:

- work is being performed directly on `main`;
- committed merge-conflict markers;
- credentials, secrets, tokens, or private keys in source control;
- unrelated Linear issues mixed in one branch or PR;
- architecture previously rejected by the product owner;
- known tenant-isolation or authorization failure;
- destructive schema work without an explicit data and rollback plan;
- a branch whose state cannot be understood confidently.

Do not run destructive commands, database writes, migrations, Git resets, force pushes, or file deletions without explicit approval. Work read-only by default during review.

# Scope and implementation discipline

Each branch and pull request must represent one cohesive Linear issue.

- Use branches named `rbj--<issue>-<description>`.
- Use PR titles named `RBJ-<issue>: <description>`.
- Do not push directly to `main`.
- Prefer small, cohesive PRs and squash merging.
- Do not mix unrelated cleanup into feature work.
- Do not introduce speculative abstractions, wrappers, dependencies, or patterns.
- Reuse existing shared components and established conventions.
- Prefer the smallest complete implementation, not the smallest diff that only handles the happy path.
- Change unrelated files only when required for compilation, validation, generated artifacts, documentation, or complete feature behavior.

When existing code is unsafe or broken, surface the finding immediately with severity, evidence, affected files, recommended correction, and whether regression testing is justified.

# Architecture ownership

Preserve clear boundaries between frontend, backend, infrastructure, persistence, domain logic, and external integrations.

The implementation agent must automatically review for:

- architectural drift and hidden coupling;
- duplicated logic and duplicated state;
- dead or unused functionality;
- oversized services or components;
- business logic placed in endpoints or UI components;
- infrastructure concerns leaking into application services;
- frontend authorization being treated as a security boundary.

Do not create an abstraction until there is a concrete need. One anticipated future consumer is not sufficient justification.

# Backend rules

## API response pattern

All services and endpoints follow Ardalis.Result.

### Service layer

Services return `Result<T>` or `Result` for operations without a value. Never define custom result wrappers.

```csharp
public interface IUserService
{
    Task<Result<UserResponse>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<Result<UserResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<UserResponse>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
```

| Situation | Return |
|---|---|
| Success | `Result<T>.Success(value)` or `Result.Success()` |
| FluentValidation failure | `Result<T>.Invalid(errors)` |
| Not found | `Result<T>.NotFound()` |
| Conflict | `Result<T>.Conflict("error_code")` |
| Forbidden | `Result<T>.Forbidden()` |

Map FluentValidation failures with Ardalis validation errors:

```csharp
var validationResult = await validator.ValidateAsync(request, cancellationToken);
if (!validationResult.IsValid)
{
    var errors = validationResult.Errors
        .Select(e => new ValidationError
        {
            Identifier = e.PropertyName,
            ErrorMessage = e.ErrorMessage
        })
        .ToList();
    return Result<ResponseType>.Invalid(errors);
}
```

### Endpoint layer

Endpoints remain thin and map service results through `ResultExtensions.ToHttpResult`.

```csharp
using Workslip.Api.Helpers;

group.MapPost("/", async (CreateRequest request, IService service, CancellationToken cancellationToken) =>
{
    var result = await service.CreateAsync(request, cancellationToken);
    return ResultExtensions.ToHttpResult(result);
});
```

A mapper may be supplied for endpoint-owned presentation such as JWT generation:

```csharp
group.MapPost("/verify-code", async (VerifyCodeRequest request, IAuthService service, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var result = await service.VerifyLoginCodeAsync(request, cancellationToken);
    return ResultExtensions.ToHttpResult(result, user => JwtHelper.GenerateToken(user, configuration));
});
```

| Result status | HTTP response |
|---|---|
| `Ok` / `Created` | `200 OK` with body |
| `Invalid` | `400` validation problem |
| `NotFound` | `404` |
| `Conflict` | `409` with `{ error, message }` |
| `Unauthorized` | `401` |
| `Forbidden` | `403` |
| `NoContent` | `204` |
| other | `500` |

Rules:

1. Never define custom `ServiceResult<T>`, `ServiceResultStatus`, or replacement validation-error types.
2. Never duplicate result-to-HTTP mapping in endpoints.
3. Do not call `Results.BadRequest`, `Results.NotFound`, or `Results.Ok` directly except for caching, list endpoints, or genuinely non-Result responses.
4. Success responses use `200 OK`, not `201 Created`.
5. Do not add redundant `SetNoStore` calls when global write-response policy already provides it.

## Backend review checklist

For every meaningful backend change, review as applicable:

- tenant isolation and IDOR risk;
- authentication and authorization;
- role escalation and permission revocation;
- transaction boundaries and rollback;
- EF Core tracking, query translation, execution strategies, and concurrency;
- retries, replay behavior, and idempotency;
- partial failures across SQL, Entra, email, storage, and other integrations;
- orphaned data and deletion behavior;
- sensitive-data logging;
- bounded queries, pagination, indexes, and N+1 behavior;
- cancellation propagation and timeout behavior.

No feature is complete when only the successful path has been considered.

# Frontend rules

Reuse shared UI, form, query, and API-access patterns.

For every frontend change, preserve:

- accessibility and keyboard behavior;
- responsive and mobile behavior;
- loading, empty, disabled, and error states;
- duplicate-action protection;
- query-cache and tenant-cache isolation;
- route and browser-back behavior;
- stale-session and authentication recovery;
- consistent generated or shared API clients;
- minimal bundle impact for rarely used features.

Avoid duplicated server state, unnecessary effects, oversized components, and direct API access that bypasses established clients.

## Form components

Use shared components in `src/FE/src/components/forms/`. Do not create raw inputs when a dedicated component exists.

### Numeric input

Never use raw `<input type="number" />`. Use `NumericInput`; mobile browsers can strip Danish decimal commas from native number fields.

```tsx
import { NumericInput } from '../../../components/forms/NumericInput';

<NumericInput
  id="hours"
  kind="decimal"
  min={0}
  max={24}
  value={draft.hours}
  onChange={(value) => updateDraft({ hours: value })}
/>

<NumericInput
  kind="integer"
  value={count}
  onChange={(value) => setCount(value)}
/>
```

Normalization remains the caller's responsibility; follow the established `parseHours` pattern.

| Component | Purpose |
|---|---|
| `ValidatedInput` | Text input with validation message |
| `NumericInput` | Danish-compatible decimal or integer input |
| `Checkbox` | Standard checkbox with label |
| `SingleSelectDropdown` | Single-select picker |
| `MultiSelectDropdown` | Multi-select picker |

# Security, integrity, and scalability gate

Every implementation must include an explicit risk review proportional to the change.

## Security

Review authentication, authorization, tenant isolation, IDOR, token storage and lifetime, replay, secret exposure, cache leakage, logging, unsafe defaults, and frontend-only enforcement.

## Data integrity

Review what happens when:

- persistence succeeds and an external integration fails;
- the external integration succeeds and persistence fails;
- the same request is retried;
- concurrent requests target the same state;
- a delete or update is partially completed;
- existing production data violates a proposed constraint.

Use transactions, execution strategies, concurrency checks, idempotency, or compensation only where the actual failure mode requires them.

## Scalability and performance

Prevent unbounded queries, N+1 calls, full-table loading, missing pagination, cross-tenant scans, duplicate frontend requests, tenant-unsafe cache keys, and unnecessary eager loading.

Do not introduce queues, distributed caching, new persistence layers, or other scaling infrastructure without a verified bottleneck or concrete expected load.

# Testing and validation truth

Code inspection is not testing. Static reasoning is mandatory, but it is insufficient for runtime behavior.

The implementation agent must distinguish these validation levels precisely:

1. **Static review** — inspect source, diffs, contracts, configuration, security boundaries, and likely failure paths.
2. **Compilation and static tooling** — restore dependencies, compile Release builds, run linting, TypeScript checks, analyzers, and generated-artifact consistency checks.
3. **Automated behavioral tests** — run focused unit, service, authorization, persistence, concurrency, and regression tests.
4. **Integration validation** — run the API with the real dependency type or the closest isolated equivalent; exercise HTTP contracts, relational database behavior, authentication, and external integration boundaries.
5. **Browser and UI validation** — launch the frontend and backend, use Playwright or an equivalent browser, click the actual controls, submit forms, navigate routes, inspect visible states, and check browser console and network failures.
6. **Deployed smoke validation** — after deployment when within scope, verify the critical flow in the target environment without destructive production testing.

## Minimum validation by change type

| Change type | Required minimum |
|---|---|
| Documentation-only | Static review and document tooling if available |
| Backend business logic | Release build plus focused behavioral tests |
| Authorization or tenant boundary | Release build, focused authorization/tenant tests, and HTTP integration where available |
| EF Core or schema behavior | Release build and relational-database tests; validate migration/schema impact against existing data |
| API contract | Release build, focused endpoint tests, OpenAPI/client consistency, and HTTP smoke |
| Frontend component | Lint, TypeScript, production build, and focused component/browser validation |
| Routing, forms, authentication, or critical UI flow | Full browser validation that presses the actual controls and verifies success and failure states |
| External integration | Contract/fake tests plus an isolated real smoke when safe and authorized |
| Infrastructure | Syntax/template validation and plan/what-if; deployment verification only when explicitly in scope |

## UI testing requirements

For UI behavior, do not stop after reading code or confirming that TypeScript compiles.

When browser tooling and a runnable environment are available:

- start the required backend and frontend services;
- use Playwright or an equivalent browser automation tool;
- authenticate through an appropriate non-production account;
- click the real buttons and links;
- verify loading, disabled, success, error, empty, and recovery states relevant to the change;
- inspect console errors, failed network requests, redirects, and cache behavior;
- test at least one mobile viewport for mobile-sensitive changes;
- capture evidence for unexpected behavior when useful.

A browser test that never interacts with the changed control does not validate the feature.

## Test selection

Add tests where they provide meaningful regression protection:

- complex business rules and calculations;
- branching workflows and state transitions;
- authorization and tenant isolation;
- transactions, rollback, retries, and idempotency;
- concurrency;
- integration boundaries;
- critical edge cases and verified regressions.

Do not add tests for trivial getters, simple mappings, framework behavior, basic CRUD pass-through, or implementation details without concrete risk. Do not optimize for coverage percentage.

## Completion language

Never use “done”, “works”, “validated”, or equivalent without stating what actually ran.

Report validation using explicit categories:

- implemented;
- statically reviewed;
- compiled;
- automated tests passed;
- integration-tested;
- browser-tested;
- deployed smoke-tested.

When a required level could not run, state exactly why and list the remaining command or flow. Unexecuted tests are evidence of intended coverage, not evidence that the implementation works.

A runtime feature that has only been statically inspected must be reported as **implemented but unvalidated**, not complete.

# Product-owner interruption policy

Ask the product owner only when the answer changes functionality, user-visible behavior, commercial expectations, legal requirements, backward compatibility, irreversible data semantics, or an accepted reliability trade-off.

Examples that require product input:

- whether one organization may see another organization's data;
- whether deletion removes or preserves history;
- whether a workflow must be reversible;
- whether backward compatibility may be broken;
- whether availability or strict correctness wins during an external outage;
- whether a major rewrite is an acceptable investment.

Do not ask the product owner to choose class names, folder placement, repository patterns, transaction design, validation libraries, test structure, cache internals, or error-mapping conventions.

# Documentation and decision recording

- Never describe proposed, experimental, or unverified behavior as implemented.
- Mark planned or historical material explicitly.
- Prefer updating an active document over creating a parallel source for the same subject.
- API, authentication, infrastructure, dataflow, database, release, or critical-flow changes must update affected documentation in the same PR, or include a documented waiver with owner and expiry.
- Generated documentation and generated API clients must not be hand-edited. Update their source and regenerate them.
- Keep documentation changes linked to the relevant Linear issue.
- Record significant architecture decisions as ADRs.
- Update repomix through the established process when repository changes make it stale.
- Important decisions made in chat must be recorded in the repository or Linear.

# Available local tools

## ripgrep

Preferred fast code search:

```text
rg "pattern" --include "*.cs"
rg -l "pattern"
rg "pattern" --context 3
rg "pattern" -g "!tests/*"
```

## repomix

Creates focused repository context:

```text
repomix --include "**/*.cs" --output context.txt
```

## Cody CLI

Semantic search when Sourcegraph authentication is available:

```text
cody search "semantic query here"
```
