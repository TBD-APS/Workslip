# Endpoint catalog

**Contract build reviewed:** 2026-07-30<br>
**Source:** endpoint registration under `src/BE/WorkslipApi/Endpoints`  
**Executable examples:** `src/BE/WorkslipApi/Postman/postman_collection.json`

The Postman item with the same route is the maintained request/response example. Parameterized requests use collection variables such as `jobId`, `userId`, `customerId`, `organizationId` and `worksheetId`.

## Platform

| Method | Path | Access | Typical response |
|---|---|---|---|
| GET | `/health` | Anonymous | `200 { "status": "ok" }` |
| GET | `/openapi/v1.json` | Currently mapped by startup | OpenAPI JSON for the running build |
| GET | Scalar UI path | Currently mapped by startup | Interactive API reference |

OpenAPI/Scalar exposure must be verified per environment. The current startup mapping is not guarded by an active development check.

## Organizations

| Method | Path | Access | Request/response |
|---|---|---|---|
| GET | `/api/organizations/` | Superadmin | All organizations ordered by name and CVR |
| POST | `/api/organizations/` | Superadmin | Organization onboarding request → organization and initial-admin view |
| POST | `/api/organizations/{organizationId}/session` | Superadmin | Selected organization → 15-minute delegated `AuthTokenResponse` |
| PUT | `/api/organizations/{organizationId}/admin` | Superadmin | Administrator email, display name and optional phone → created or updated admin view |

These routes support the frontend `/superadmin` page. The admin upsert rejects an email owned by another organization, protects existing `Superadmin` accounts from demotion, sends a Microsoft Entra B2B invitation for new identities, assigns the Entra `Admin` app role and returns `entraInvitationSent` so the UI can distinguish a newly sent invitation from reuse of an existing identity.

The delegated session endpoint does not modify the Superadmin user row or create organization memberships. It verifies the target organization and the actor's current database role, then returns a short-lived token whose `organizationId` is the selected organization while the real Superadmin user ID and role are preserved. The original token is restored when the frontend exits the session or the delegated token expires.

## Authentication and invitations

| Method | Path | Access | Request/response |
|---|---|---|---|
| GET | `/api/auth/me` | Read | Current user view; delegated Superadmins receive the effective organization ID |
| PATCH | `/api/auth/me` | User | Profile update → user view |
| POST | `/api/auth/send-code` | Anonymous | `{ "email": "..." }` → generic `200` message |
| POST | `/api/auth/verify-code/{code}` | Anonymous | Email body + code path → local bearer token |
| POST | `/api/auth/entra-enroll` | Entra JWT | Enrollment request → local bearer token |
| POST | `/api/auth/entra-login` | Entra JWT | Authenticated Entra identity → local bearer token |
| GET | `/api/auth/invites` | Admin | Organization invitation list |
| DELETE | `/api/auth/invites/{inviteId}` | Admin | Clears one tenant-owned status; revokes pending invite-owned Entra guest |
| POST | `/api/auth/invite` | Admin | Invitation batch → result |
| POST | `/api/auth/invite/{token}/open` | Anonymous | Marks invitation opened |

## Users

All `/api/users` routes are in the admin route group. Additional user requirements do not reduce the effective admin requirement. User list/detail responses retain the canonical `role` and add the Danish presentation field `roleDisplayName`.

| Method | Path | Access | Request/response |
|---|---|---|---|
| POST | `/api/users/` | Admin | Create user → user view |
| GET | `/api/users/` | Admin | `limit`, `offset`, `search`, `sortBy`, `sortDirection` → paginated user list |
| GET | `/api/users/{id}` | Admin | User detail |
| PATCH | `/api/users/{id}` | Admin | Update user → user view |
| DELETE | `/api/users/{id}` | Admin | `204` or mapped error |

## Jobs

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/jobs/` | Read | Filters, repeated `status`, sorting and pagination → `{ items, totalCount }` |
| GET | `/api/jobs/my-assigned` | Read | Current user's assigned jobs |
| GET | `/api/jobs/{id}` | Read | Job summary; supports ETag revalidation |
| GET | `/api/jobs/{id}/history` | Read | `limit`/`offset` → history events |
| GET | `/api/jobs/{id}/report/pdf` | Read | PDF file response |
| POST | `/api/jobs/` | User | Requires `Idempotency-Key`; create → job summary |
| PATCH | `/api/jobs/{id}` | User | Requires `Idempotency-Key`; update → job summary |
| POST | `/api/jobs/{id}/status` | User | Requires `Idempotency-Key`; status request → job summary |
| POST | `/api/jobs/{id}/seen` | User | Marks job seen → `204` |
| POST | `/api/jobs/{id}/assign` | User | `{ "userIds": [...] }` → job summary |
| DELETE | `/api/jobs/{id}` | Admin | `204`, `404` or deletion conflict |
| POST | `/api/jobs/{id}/restore/deletion` | Admin | Restores scheduled deletion → job summary |
| POST | `/api/jobs/{id}/links` | User | Link request → job summary |
| DELETE | `/api/jobs/{id}/links` | User | Link deletion request → mapped result |

Job status values implemented by the current domain are `Draft`, `InReview`, `Approved` and `Rejected`.

The status endpoint remains in the user route group because ordinary users submit work through it. The application service enforces this role-aware transition matrix before persistence, history or notification side effects:

| Current status | Target status | Allowed roles |
|---|---|---|
| `Draft` | `InReview` | User, Admin, Superadmin |
| `Rejected` | `InReview` | User, Admin, Superadmin |
| `InReview` | `Approved` | Admin, Superadmin |
| `InReview` | `Rejected` | Admin, Superadmin |
| Same status | Same status | Roles authorized for that target; treated idempotently |

Targeting `Draft` or any other source/target combination returns `409 Conflict`. A caller lacking permission for the requested target returns `403 Forbidden`. Job lookup remains scoped to the effective organization, so a cross-tenant ID returns `404` before role details are evaluated.

## Customers

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/customers/search` | Read | `query`, `limit` → search results |
| GET | `/api/customers/favorite` | Read | Optional `limit`, default `3` → favorite customer search results |
| GET | `/api/customers/` | User | Paginated customer list |
| GET | `/api/customers/{id}` | User | Customer detail including `customerNumber`, address, ZIP, city and country |
| POST | `/api/customers/` | Admin | Requires `Idempotency-Key`; create → detail |
| PUT | `/api/customers/{id}` | Admin | Update → detail |
| PATCH | `/api/customers/{id}/favorite` | Admin | `{ "isFavorite": true or false }` → mapped result |
| DELETE | `/api/customers/{id}` | Admin | `204` or mapped error |
| POST | `/api/customers/import` | Admin | Multipart `.xlsx`/`.csv`, max 10 MB, rate limited → imported/duplicate/skipped/failed counts and row errors |

Customer imports map `Nr.` to `customerNumber`, preserve separate address/ZIP/city/country fields, and ignore unrelated source columns such as group and customer reference.

## Worksheets

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/worksheets/jobs/{jobId}` | User | Upsert worksheet → job summary |
| DELETE | `/api/worksheets/{worksheetId}/jobs/{jobId}` | User | Delete worksheet → job summary |
| GET | `/api/worksheets/my` | User | Optional `year`, `month` → current-user month |
| GET | `/api/worksheets/all` | Admin | Optional `year`, `month` → organization month |

## Reference data

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/reference-data/` | Read | Reference-data response with installation types ordered alphabetically by name and ETag/`304` support |

## Notifications and push

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/push-subscriptions/` | User, excluding Superadmin | Browser push endpoint and keys → mapped result |
| GET | `/api/notifications/` | User | `limit`, `offset` → notification history |
| PATCH | `/api/notifications/{id}/read` | User | Marks one notification read → `204` |
| POST | `/api/notifications/read-all` | User | Marks all read → `204` |
| DELETE | `/api/notifications/{id}` | User | Deletes one owned notification → mapped result |

Superadmins are blocked from push registration so a device used during a delegated organization session is never attached to a tenant notification stream.

## Operations

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/admin/cache/clear` | Admin | Clears application caches and attempts Vercel cache invalidation |

## Development endpoints

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/dev/token` | Anonymous in current route definition | Generates a local token by email; must never be treated as safe outside isolated development |
| GET | `/api/dev/debug` | Authenticated | Returns identity/claim diagnostics |

The current application maps these endpoints through `ConfigureDevEnvironment`, but the environment guard is commented out. This is a verified implementation risk, not a documentation assumption.
