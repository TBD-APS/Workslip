# Unified API Response Pattern with Ardalis.Result

## Problem

The API uses 4 inconsistent patterns for returning results from services and mapping them to HTTP responses:

1. `JobServiceResult<T>` + custom enum (`Workslip.Application/Jobs/JobService.cs`)
2. `OrganizationServiceResult<T>` (extended variant with ErrorCode/Message) (`Workslip.Application/Organizations/OrganizationService.cs`)
3. Tuple returns `(bool Success, T? Value, ...)` (`Workslip.Application/Users/UserService.cs`)
4. Direct inline returns with anonymous objects (`Endpoints/AuthEndpoints.cs`)

This means:
- Frontend gets different error shapes depending on the endpoint
- Each endpoint file duplicates HTTP mapping logic
- Service layer has 3 custom result type definitions
- Adding a new feature requires writing yet another result type

## Solution: Ardalis.Result

Use `Ardalis.Result` as the standard return type for all Application service methods, with a shared `ToHttpResult` helper in the Api layer for minimal API mapping.

### Core types (from library)

```
Result<T>         — generic result with Value
Result            — non-generic result (void operations)
ValidationError   — field-level error (Identifier, ErrorMessage, ErrorCode, Severity)
ResultStatus      — enum (Ok, NotFound, Invalid, Conflict, Unauthorized, Forbidden, NoContent, Error, CriticalError)
```

### Service layer pattern

Every service method returns `Task<Result<T>>` (or `Task<Result>` for void operations):

- `Result<T>.Success(value)` → 200/201
- `Result<T>.Invalid(errors)` → 400 with validation errors
- `Result<T>.Conflict()` → 409
- `Result<T>.NotFound()` → 404
- `Result<T>.Unauthorized()` → 401
- `Result<T>.Forbidden()` → 403
- `Result<T>.NoContent()` → 204
- `Result<T>.Error(message)` → 500

### HTTP mapping: shared helper

A single `ResultExtensions.ToHttpResult(Result<T>, locationFunc?)` maps all result statuses to `IResult` (Minimal API). Endpoints become one-liners.

### Error envelope sent to frontend

Success: `{ data: T }` with HTTP 2xx
Validation error: `{ errors: Record<string, string[]> }` via `ValidationProblem()` (RFC 7807)
Domain error: `{ error: string, message: string }` with appropriate HTTP status

## Implementation Plan

1. Add `Ardalis.Result` NuGet to Api and Application projects
2. Remove `FluentResults` from Application project
3. Create shared `ToHttpResult` helper in Api layer
4. Refactor Organizations (service + endpoint) as first example
5. Refactor Jobs (service + endpoint)
6. Refactor Users (service + endpoint)
7. Remove obsolete custom types: `OrganizationServiceResult<T>`, `OrganizationServiceResultStatus`, `OrganizationValidationError`, `JobServiceResult<T>`, `JobServiceResultStatus`
