# Cache diagnostics

**Status:** Active
**Owner:** Workslip maintainers
**Source of truth:** Runtime cache instrumentation, API endpoint registrations, frontend diagnostics page, and executable validation
**Review cadence:** When cache regions, expiry policies, or cache invalidation behavior change

## Purpose

Workslip exposes cache metadata to Superadmin so cache behavior can be inspected without exposing cached business data, authentication material, tenant identifiers, or full cache keys.

## Access

The diagnostics UI is available at `/superadmin/cache` and requires the existing Superadmin permission boundary.

Backend endpoints:

- `GET /api/superadmin/cache/status`
- `POST /api/superadmin/cache/clear`

The existing `POST /api/admin/cache/clear` endpoint remains available for backward-compatible deployment automation.

All diagnostics responses use `Cache-Control: no-store`.

## Backend regions

| Region | Cache type | Expiry | Instrumented behavior |
|---|---|---:|---|
| `reference-data` | HybridCache | 10 minutes local | hit, miss, set, load duration, failure, invalidation |
| `authenticated-users` | IMemoryCache | 1 hour absolute | hit, miss, set, load duration, failure, invalidation |

Counters are process-local and reset when the API instance restarts. They are operational signals, not billing or audit records.

## Frontend diagnostics

The Superadmin page shows:

- React Query count, status, fetch state, staleness, observer count, and update time;
- service-worker registration state;
- Cache Storage names and entry counts;
- browser storage usage estimate when supported;
- backend cache counters and the API instance start/clear timestamps.

React Query keys are reduced to a safe top-level scope. Full keys and cached values are not displayed.

## Clearing caches

The Superadmin clear action:

1. invalidates HybridCache entries tagged `all`;
2. compacts the process IMemoryCache;
3. attempts to purge the configured Vercel edge-cache tag;
4. clears frontend React Query entries;
5. deletes browser Cache Storage entries.

The service worker registration remains installed. Deleting its caches is sufficient for the next requests to repopulate current assets without removing PWA capability.

## Security constraints

Diagnostics must never return or render:

- cached values;
- access tokens, invite tokens, secrets, or integration credentials;
- e-mail addresses or user identifiers;
- customer, job, report, or worksheet payloads;
- complete tenant-, user-, search-, or entity-specific cache keys.

Frontend authorization is only a navigation control. The backend Superadmin policy is the security boundary.
