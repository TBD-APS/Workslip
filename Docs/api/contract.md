# Workslip API contract

**State:** Maintained  
**Owner:** Backend/API  
**Review:** With every endpoint, auth or response-shape change  
**Linear:** WOR-146, WOR-193

## Source of truth

Use these sources in this order:

1. Endpoint and contract source under `src/BE/WorkslipApi`.
2. Runtime OpenAPI document at `/openapi/v1.json` for the exact deployed build.
3. `Postman/postman_collection.json` for executable request examples and smoke assertions.
4. This guide for conventions and integration behaviour.

OpenAPI and Scalar are currently mapped by application startup. They must be treated as an operationally sensitive surface until environment restriction is verified. Dev endpoints are also mapped by the current startup code; do not assume that their names alone make them development-only.

## Authentication and authorization

Send local Workslip or accepted Entra access tokens as:

```http
Authorization: Bearer <token>
```

Policy meanings:

| Policy | Intended access |
|---|---|
| Anonymous | No bearer token required. |
| `RequireReadAccess` | Authenticated read-capable role, including auditor where configured. |
| `RequireUser` | Normal operational user or a higher configured role. |
| `RequireAdmin` | Admin or a higher configured role. |
| `RequireSuperAdmin` | Superadmin only. |

The API derives organization, user and role from authenticated claims. Integrations must not send or trust a client-selected organization ID as an authorization boundary.

## User role fields

User list, user detail and current-user responses expose two role fields:

- `role` is the canonical authorization value (`User`, `Auditor`, `Admin` or `Superadmin`). Clients must use this field for permission logic.
- `roleDisplayName` is the backend-owned Danish display label used by the UI. It is presentation data and must not be used for authorization.

The display field is additive. Clients that do not yet understand it may continue using the canonical `role` value.

## Invitation administration

Admin-authorized invitation status operations are:

```text
GET    /api/auth/invites
DELETE /api/auth/invites/{inviteId}
```

The delete operation is tenant-scoped by the authenticated organization. It removes the selected status row and invalidates an unaccepted invitation. When Workslip created an Entra guest specifically for that pending invitation, the guest is removed before the status row is deleted. Accepted invitations only have their historical status row removed; the enrolled user is not deleted.

## Standard headers

```http
Accept: application/json
Content-Type: application/json
X-Correlation-ID: <uuid-or-trace-id>
Idempotency-Key: <stable-key-for-one-logical-mutation>
```

`Idempotency-Key` is mandatory on currently protected mutation endpoints. Missing keys can return `428 Precondition Required`. Reusing a key with different content returns a conflict. A replay can return the stored original response.

## Result and error contract

Application services return `Ardalis.Result`; endpoints normally map it through `ResultExtensions.ToHttpResult`.

| Result | HTTP |
|---|---:|
| Success | `200` |
| No content | `204` |
| Invalid | `400` validation problem |
| Unauthorized | `401` |
| Forbidden | `403` |
| Not found | `404` |
| Conflict | `409` |
| Missing idempotency key | `428` |
| Unexpected failure | `500` |

Validation example:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["Email is invalid."]
  }
}
```

Conflict example:

```json
{
  "error": "duplicate_report_number",
  "message": "duplicate_report_number"
}
```

Some specialized endpoints return a direct `{ "error": "..." }` payload. Integrations must branch primarily on HTTP status and then use `error` as a stable machine-readable code where present.

## Pagination, filtering and sorting

List endpoints commonly use:

```text
limit=<positive integer>
offset=<zero or positive integer>
search=<text>
sortBy=<supported field>
sortDirection=asc|desc
```

Job listing additionally supports repeated status values and customer/report filters. Arrays are serialized as repeated query parameters:

```text
/api/jobs?status=Draft&status=InReview&limit=50&offset=0
```

Responses that are paginated use an object containing `items` and `totalCount` unless the endpoint contract states otherwise.

## Caching and correlation

Selected GET endpoints return ETags and private revalidation headers. Clients may send `If-None-Match` and handle `304 Not Modified`.

The API accepts/creates correlation identifiers and writes them to request telemetry. Preserve `X-Correlation-ID` across integration boundaries and include it in support reports.

## Compatibility

- Additive optional fields are normally backward compatible.
- Removing or renaming fields, routes, enum values or error codes requires a migration/deprecation plan.
- Generated frontend clients and the Postman collection must be reviewed with every contract change.
- Planned behaviour is never documented as deployed behaviour.
