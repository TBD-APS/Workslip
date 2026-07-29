# Workslip API contract

**State:** Maintained  
**Owner:** Backend/API  
**Review:** With every endpoint, auth or response-shape change  
**Linear:** WOR-107, WOR-146, WOR-193

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

Tenant users (`User`, `Auditor`, and `Admin`) are permanently associated with one organization. Their authenticated identity contains the server-owned `organizationId` claim, and the API ignores any client-supplied organization-scope header for those roles.

A `Superadmin` is a platform identity and has `organizationId = null` in Workslip. Its local JWT and transformed Entra identity do not contain a permanent organization claim. Dedicated Superadmin platform routes operate cross-tenant without selecting an organization.

Before a Superadmin calls ordinary tenant endpoints, the client must explicitly select an organization and send:

```http
X-Workslip-Organization-Id: <organization-guid>
```

The backend accepts this header only after authenticating the caller as `Superadmin`. Missing or malformed scope leaves the Superadmin without tenant context, causing tenant-scoped services to reject the request rather than infer or default an organization. Normal tenant users cannot use this header to escape their authenticated organization.

Clients must clear tenant-specific application caches when switching the selected organization. The first-party frontend performs a full navigation after selection to prevent React Query data from one tenant being reused in another tenant view.

## Organization administration

The following platform operations require `RequireSuperAdmin`:

```text
GET  /api/organizations/
POST /api/organizations/
PUT  /api/organizations/{organizationId}/admin
```

These operations do not require `X-Workslip-Organization-Id`. Their explicit route parameters and dedicated cross-tenant application/repository paths define the target organization.

The GET operation returns all organizations ordered by name and CVR for the `/superadmin` administration page. This is an intentional cross-tenant read and must remain inside the exclusive Superadmin route group.

Organization creation returns the organization and its initial local administrator placeholder. The administrator upsert accepts `email`, `displayName` and optional `phone`, normalizes the email address, creates a Microsoft Entra B2B invitation when needed, sends the Entra invitation message with `/login` as the redemption redirect, assigns the Entra `Admin` app role, and creates or updates the local `Admin` row in the selected organization.

The admin response includes `entraInvitationSent`. It is `true` only when a new Entra guest and invitation message were created. It is `false` when an existing Entra identity was reused and its Admin role/local record was updated.

Sequential upserts for the same organization and email are idempotent. An email already owned by another organization returns `email_in_use`, and an existing platform `Superadmin` account is never converted to tenant `Admin` (`superadmin_role_protected`). Conditional writes reject stale concurrent changes with `admin_state_changed`; clients may reload and retry. If Workslip creates a new Entra guest but SQL persistence fails, it removes that guest only when no persisted user references the identity.

Non-empty user emails are globally unique through the filtered SQL index `UX_Users_Email`, matching the identity lookup used by authentication. Schema initialization fails explicitly if legacy duplicate non-empty emails exist, because silently selecting one organization would violate tenant isolation.

## User organization and role fields

User list, user detail and current-user responses expose:

- `organizationId`: required for `User`, `Auditor`, and `Admin`; `null` for platform `Superadmin`.
- `role`: canonical authorization value (`User`, `Auditor`, `Admin`, or `Superadmin`). Clients must use this field for permission logic.
- `roleDisplayName`: backend-owned Danish display label used by the UI. It is presentation data and must not be used for authorization.

Tenant user-management endpoints reject attempts to create or convert users to `Superadmin`. Platform Superadmins are provisioned through the controlled platform identity path, not ordinary organization user CRUD.

## Invitation administration

Admin-authorized invitation status operations are:

```text
GET    /api/auth/invites
DELETE /api/auth/invites/{inviteId}
```

The delete operation is tenant-scoped by the authenticated or explicitly selected organization. A pending invitation is atomically revoked and its token rotated before any external cleanup starts, so concurrent enrollment and clearing cannot both succeed. When Workslip created an Entra guest specifically for that pending invitation, the guest is removed before the revoked status row is deleted. If Graph cleanup fails, the revoked row remains as durable retry state. Accepted invitations only have their historical status row removed; the enrolled user is not deleted.

## Standard headers

```http
Accept: application/json
Content-Type: application/json
X-Correlation-ID: <uuid-or-trace-id>
Idempotency-Key: <stable-key-for-one-logical-mutation>
X-Workslip-Organization-Id: <organization-guid>  # Superadmin tenant endpoints only
```

`Idempotency-Key` is mandatory on currently protected mutation endpoints. Missing keys can return `428 Precondition Required`. Reusing a key with different content returns a conflict. A replay can return the stored original response.

`X-Workslip-Organization-Id` is not an authorization credential. It is an explicit tenant-selection value that is honored only for an already authenticated platform Superadmin.

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

Tenant-scoped client caches must include the selected organization in their effective cache boundary or be cleared when the organization changes.

## Compatibility

- Additive optional fields are normally backward compatible.
- `organizationId` becoming nullable for `Superadmin` requires generated clients to preserve `null` rather than substituting an empty GUID.
- Removing or renaming fields, routes, enum values or error codes requires a migration/deprecation plan.
- Generated frontend clients and the Postman collection must be reviewed with every contract change.
- Planned behaviour is never documented as deployed behaviour.
