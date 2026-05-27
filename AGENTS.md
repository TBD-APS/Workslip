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
