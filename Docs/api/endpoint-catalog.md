# Endpoint catalog

**Contract build reviewed:** 2026-07-31<br>
**Source:** endpoint registration under `src/BE/WorkslipApi/Endpoints`  
**Executable examples:** `src/BE/WorkslipApi/Postman/postman_collection.json`

This catalog summarizes the maintained HTTP contract. Runtime endpoint registrations and generated OpenAPI remain authoritative.

## Authentication

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/auth/entra` | Anonymous | Exchange a valid Entra access token for a Workslip token |
| POST | `/api/auth/dev-login` | Development | Local development login |
| POST | `/api/auth/one-time-code/request` | Anonymous | Request a one-time login code |
| POST | `/api/auth/one-time-code/verify` | Anonymous | Verify a one-time login code |
| GET | `/api/auth/me` | Authenticated | Current Workslip user |
| PATCH | `/api/auth/me` | Authenticated | Update current user's profile |

## Organizations

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/organizations` | Superadmin | List customer organizations |
| POST | `/api/organizations` | Superadmin | Create organization and initial admin |
| PUT | `/api/organizations/{organizationId}/admin` | Superadmin | Invite or reconcile organization admin |
| POST | `/api/organizations/{organizationId}/session` | Superadmin | Issue short-lived delegated organization token |

## Users

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/users/` | Admin | List organization users |
| GET | `/api/users/{id}` | Admin | Get organization user |
| POST | `/api/users/invite` | Admin | Invite user |
| PUT | `/api/users/{id}` | Admin | Update user |
| DELETE | `/api/users/{id}` | Admin | Delete user |
| POST | `/api/users/{id}/resend-invite` | Admin | Resend invitation |

## Customers

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/customers/` | Read | Search/list organization customers |
| GET | `/api/customers/{id}` | Read | Get one organization customer |
| POST | `/api/customers/` | Admin | Create customer |
| PUT | `/api/customers/{id}` | Admin | Update customer |
| DELETE | `/api/customers/{id}` | Admin | Delete customer |
| POST | `/api/customers/import` | Admin | Import customers from XLSX |

Customer imports map `Nr.` to `customerNumber`, preserve separate address/ZIP/city values, validate rows before persistence, and reject duplicate customer numbers within the organization.

## Jobs

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/jobs/` | Read | Filtered and paginated job list |
| GET | `/api/jobs/my-assigned` | Read | Jobs assigned to current user |
| GET | `/api/jobs/{id}` | Read | Job summary |
| GET | `/api/jobs/{id}/history` | Read | Job history |
| GET | `/api/jobs/{id}/report/pdf` | Read | Generated PDF report |
| POST | `/api/jobs/` | User | Create job; requires idempotency key |
| PATCH | `/api/jobs/{id}` | User | Update job; requires idempotency key |
| POST | `/api/jobs/{id}/status` | User | Change status; requires idempotency key |
| POST | `/api/jobs/{id}/seen` | Read | Mark job as seen |
| DELETE | `/api/jobs/{id}` | Admin | Delete job |
| POST | `/api/jobs/{id}/restore/deletion` | Admin | Undo deletion |
| POST | `/api/jobs/{id}/assign` | User | Assign users |

## Worksheets

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/worksheets/job/{jobId}` | User | Job worksheets |
| GET | `/api/worksheets/{id}` | User | Worksheet detail |
| POST | `/api/worksheets/` | User | Create worksheet |
| PUT | `/api/worksheets/{id}` | User | Update worksheet |
| DELETE | `/api/worksheets/{id}` | User | Delete worksheet |
| GET | `/api/worksheets/mine` | User | Current user's worksheets |
| GET | `/api/worksheets/all` | Admin | Optional `year`, `month` → organization month |

## Reference data

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/reference-data/` | Read | Reference-data response with installation types ordered alphabetically by name and ETag/`304` support |

## Notifications and push

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/push-subscriptions/public-key` | User | Active private-key-derived VAPID public key; `Cache-Control: no-store` |
| POST | `/api/push-subscriptions/` | User, Admin, Superadmin | Browser endpoint and keys; optional `replacedEndpoint` deactivates exactly one stale subscription |
| GET | `/api/notifications/` | User | `limit`, `offset` → notification history |
| PATCH | `/api/notifications/{id}/read` | User | Marks one notification read → `204` |
| POST | `/api/notifications/read-all` | User | Marks all read → `204` |
| DELETE | `/api/notifications/{id}` | User | Deletes one owned notification → mapped result |

The backend derives the active public key from `Vapid:PrivateKey`. During authenticated startup, the frontend compares the browser subscription's `applicationServerKey` with this endpoint. A mismatch recreates the browser subscription and supplies the old endpoint as `replacedEndpoint`, preserving other device subscriptions. Push subscriptions, queued notifications, history and delivery lookup are keyed by the authenticated actor's `UserId`, so Superadmins register normally. Entering a delegated organization session changes effective tenant access but does not broaden which notification rows target the actor.

## Operations

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/admin/cache/clear` | Admin | Clears application caches and attempts Vercel cache invalidation |

## Development endpoints

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/dev/seed` | Development | Seed development data |
