# Agent tool policy

Before answering questions about this repository, prefer local tools over guessing.

## Required lookup order

1. Use repomix and kioki to search through files fast.
3. Use database/schema tools before answering questions about tables, columns, EF mappings, migrations, seed data, or SQL behavior.
4. Use documentation tools such as Context7 before answering package/API-specific questions about EF Core, ASP.NET Core, Microsoft Graph, authentication, or frontend libraries.
5. Use browser/testing tools such as Playwright when the task involves UI behavior, routing, forms, or end-to-end validation.

## Do not guess

If local source, schema, or package docs are available through MCP, inspect them first.
If the tool is unavailable, say that and continue with best-effort reasoning.

## Safety

Do not run destructive commands, database writes, migrations, Git resets, force pushes, or file deletions without explicit user approval.
Prefer read-only inspection unless the task clearly requires changes.

# API Response Pattern

All services and endpoints follow Ardalis.Result for consistent API responses.

## Service Layer

Services return `Result<T>` (or `Result` for void operations). Never define custom result wrappers.

```csharp
public interface IUserService
{
    Task<Result<UserResponse>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<Result<UserResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<UserResponse>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
```

### Return values

| Situation | Return |
|---|---|
| Success | `Result<T>.Success(value)` or `Result.Success()` |
| FluentValidation failure | `Result<T>.Invalid(errors)` — map `ValidationFailure` to `new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage }` |
| Not found | `Result<T>.NotFound()` |
| Conflict | `Result<T>.Conflict("error_code")` |
| Forbidden | `Result<T>.Forbidden()` |

### Validation error mapping pattern

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

## Endpoint Layer

Endpoints call `ResultExtensions.ToHttpResult(result)` — never inline result-to-response mapping.

```csharp
using Workslip.Api.Helpers;

group.MapPost("/", async (CreateRequest request, IService service, CancellationToken cancellationToken) =>
{
    var result = await service.CreateAsync(request, cancellationToken);
    return ResultExtensions.ToHttpResult(result);
});
```

### With a mapper (e.g. JWT token generation)

```csharp
group.MapPost("/verify-code", async (VerifyCodeRequest request, IAuthService service, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var result = await service.VerifyLoginCodeAsync(request, cancellationToken);
    return ResultExtensions.ToHttpResult(result, user => JwtHelper.GenerateToken(user, configuration));
});
```

### What ToHttpResult maps to

| Result status | HTTP response |
|---|---|
| `Ok` / `Created` | `200 OK` with body |
| `Invalid` | `400` with `ValidationProblem` (field-level errors) |
| `NotFound` | `404` |
| `Conflict` | `409` with `{ error, message }` |
| `Unauthorized` | `401` |
| `Forbidden` | `403` |
| `NoContent` | `204` |
| any other | `500` |

## Rules

1. Never define custom `ServiceResult<T>`, `ServiceResultStatus`, or `ValidationError` types — use Ardalis.Result.
2. Never inline result-to-HTTP mapping in endpoints — use `ToHttpResult`.
3. Never call `Results.BadRequest`, `Results.NotFound`, `Results.Ok` directly in endpoints (except caching, list endpoints, or non-Result responses).
4. All success responses are `200 OK` (not `201 Created`) — no `Location` headers.
5. `SetNoStore` is unnecessary on write endpoints — remove it.

# Available Tools

## ripgrep (rg)
Fast code search. Preferred over `grep`/`Select-String`.
```
rg "pattern" --include "*.cs"         # search C# files
rg -l "pattern"                       # list matching files only
rg "pattern" --context 3              # show context
rg "pattern" -g "!tests/*"           # exclude tests
```

## repomix
Packs repo into single file for AI context sharing.
```
repomix --include "**/*.cs" --output context.txt
```

## Cody CLI (@sourcegraph/cody)
AI-powered code search. Requires Sourcegraph login (`cody login`).
```
cody search "semantic query here"
```
