# Application Insights error dashboard

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** `DiagnosticsEndpoints`, `ApplicationInsightsErrorDiagnosticsService`, frontend Application Insights bootstrap, Azure monitoring configuration and the Superadmin diagnostics UI  
**Review cadence:** On telemetry, Azure RBAC, KQL schema, error-handling or incident-process changes

## Purpose

The Superadmin error dashboard gives an operational view of recent Workslip frontend and backend failures without requiring Azure Portal access. It is read-only and complements, but does not replace, Azure Monitor alerts, Application Insights investigation or the incident process.

The dashboard is loaded only when a Superadmin opens it. Ordinary tenant users and normal application routes do not query Log Analytics.

## Trust invariant

The dashboard may report **current**, **partial**, **stale** or **unavailable** data. It must never silently convert an invalid, incomplete or failed Azure response into a trustworthy-looking zero or empty list.

A zero error count is trustworthy only when all of these conditions are true:

- the summary query completed and passed schema validation;
- the detail query completed and passed schema validation;
- Azure did not mark any result as partial;
- telemetry-health data was queried successfully;
- the UI contract passed runtime validation;
- the view is not marked stale.

Frontend and backend telemetry timestamps are displayed separately. A missing or old timestamp means that the corresponding pipeline has not recently been observed; it does not prove that the application has no errors.

No in-app dashboard can remain current during a total Azure/query outage. During such an outage Workslip shows the last complete in-memory snapshot for up to one hour, clearly marked stale. If no complete snapshot exists, the dashboard shows partial or unavailable state rather than fabricated data. The in-memory snapshot is lost when the API restarts.

## Data flow and trust boundary

1. A minimal HTML listener captures up to 20 errors that occur before JavaScript modules finish evaluating.
2. The frontend installs sanitized global error handlers before React renders.
3. Application Insights initialization is deferred for startup performance, retries twice after transient initialization failures, then flushes buffered errors.
4. The frontend emits a sanitized heartbeat at initialization, every five minutes while visible and when the app becomes visible again.
5. The API records Application Insights request telemetry and structured Serilog traces with correlation metadata.
6. The API managed identity obtains an Azure token for the Log Analytics query API.
7. The API executes fixed, version-controlled KQL against the configured workspace.
8. The API validates Azure response shape, detects partial results, maps only allowlisted columns, sanitizes fields again and groups repeated failures by a non-reversible fingerprint.
9. The frontend validates the complete API response at runtime before rendering it.

The browser never receives Azure credentials, workspace access tokens, KQL, raw table rows, stack traces, request or response bodies, headers or complete custom dimensions.

## Authorization and Azure RBAC

`GET /api/admin/diagnostics/errors` requires the existing Superadmin policy and the `diagnostics-read` rate-limit policy. The response is marked `Cache-Control: no-store`.

The API managed identity receives the built-in **Log Analytics Data Reader** role scoped to the Workslip Log Analytics workspace. The workspace customer ID is stored as non-secret configuration under:

`Azure:ApplicationInsights:WorkspaceId`

Do not replace this with a workspace key, shared secret or administrator credential. Do not widen the role assignment to the resource group or subscription without a verified need.

## Query contract

The client may select only:

- range: `1h`, `24h` or `7d`;
- source: `all`, `frontend` or `backend`;
- limit: `10` through `100`.

The service rejects any other value before contacting Azure. It does not accept client-supplied KQL.

Three independent queries are executed concurrently:

1. error summary;
2. grouped error details;
3. telemetry-pipeline health.

A failure in one query does not erase successful sections. Missing sections are explicitly unavailable and are never replaced with zero or an empty list.

### Error sources

The fixed queries use:

- `AppExceptions` for frontend exceptions emitted by Workslip browser telemetry;
- `AppTraces` at error or critical severity for explicit backend errors;
- structured Serilog request-completion traces with HTTP status `>= 500` for controlled backend failures that may not throw an exception.

A request-completion 5xx event is excluded when the same Application Insights operation already has an explicit backend error trace, preventing double counting.

Counts use Application Insights `ItemCount` as their weight. This preserves occurrence totals when ingestion sampling represents multiple original telemetry items with one stored row.

The source filter applies to both summary and details. Details are grouped in KQL, sanitized again in the API and grouped once more by safe fingerprint after redaction. The API detects when its bounded result set is truncated and exposes that state to the UI.

### Pipeline health

Pipeline health uses:

- `AppEvents` with event name `telemetry.heartbeat` for frontend activity;
- `AppRequests` for backend request telemetry.

The API returns the latest observed UTC timestamp for each pipeline. Null timestamps are valid query results and are displayed as “not observed”, not as query failures.

Health age is calculated against the API-generated UTC timestamp, not the user device clock.

## Azure response handling

The Logs Query API may return HTTP 200 while also marking the result with `error.code = PartialError`. Workslip detects this and marks the dashboard incomplete.

Every query response must contain a valid `PrimaryResult` table with all expected columns and correctly typed rows. Missing columns, malformed rows, invalid timestamps, invalid enum values or invalid counts result in `invalid_response`. They are never interpreted as empty data.

Transient network errors and HTTP 408, 429 or 5xx responses are retried once where bounded retry is safe. Request cancellation propagates immediately. Query timeouts are not multiplied through repeated 15-second attempts.

Raw Azure error bodies are neither logged nor returned.

## Last-known-good behavior

A dashboard snapshot is cached only when summary, details and telemetry health all complete successfully and Azure does not report a partial result.

The cache key includes selected range, source and limit. A complete snapshot remains available in API memory for one hour.

When a later refresh fails:

- the last complete snapshot remains visible;
- `isStale` becomes true;
- `isComplete` becomes false;
- the original data retrieval timestamp remains unchanged;
- the current failure reason is displayed;
- the UI explicitly says that values are not current.

Partial results never replace the last complete snapshot.

## Returned fields

The API contract may contain only:

- availability, completeness, stale and truncation state;
- UTC generation and data-retrieval timestamps;
- summary counts;
- frontend/backend telemetry last-seen timestamps;
- error timestamp;
- source (`frontend` or `backend`);
- normalized severity;
- sanitized error type;
- non-reversible fingerprint;
- sanitized message;
- normalized route or operation;
- release identifier;
- safe hexadecimal correlation ID or trace ID;
- grouped occurrence count.

It must never return raw exception objects, stack traces, request or response bodies, headers, authorization values, cookies, e-mail addresses, phone numbers, tenant IDs, entity IDs or complete Application Insights properties.

## Redaction and correlation identifiers

Redaction is performed in two places:

1. frontend telemetry sanitizes browser exceptions before ingestion;
2. the diagnostics API sanitizes every returned field regardless of source.

The API removes or normalizes:

- bearer/basic credentials and common secret keys;
- token query parameters and long token-like values;
- e-mail addresses and phone numbers;
- GUID and numeric route segments;
- line breaks and excessive length;
- arbitrary or token-like correlation identifiers.

The correlation middleware accepts only 16–64 hexadecimal/hyphen characters from `X-Correlation-ID`. Any other value is replaced with a server-generated ID before logging or reflection. The dashboard applies the same restrictive output rule to historical telemetry.

Backend detail queries prefer the Serilog message template over the rendered message to reduce the risk of returning structured property values. Redaction remains mandatory after query parsing.

## Availability states

| Reason | Meaning | Operator action |
|---|---|---|
| `not_configured` | Workspace ID is missing | Verify App Configuration deployment and API refresh |
| `permission_denied` | Managed identity lacks query access | Verify workspace-scoped role assignment and propagation |
| `throttled` | Azure returned a rate-limit response | Retry and inspect query frequency if recurring |
| `timeout` | A query exceeded its bounded timeout | Check Azure health and workspace response time |
| `token_unavailable` | API could not obtain its managed-identity token | Check managed identity and Azure identity availability |
| `invalid_response` | Azure schema or row data could not be validated | Inspect the live workspace schema before changing parsers |
| `partial_result` | Azure returned only part of a result | Treat values as incomplete and investigate in Azure Portal |
| `query_failed` | Other sanitized query failure | Use Azure Portal, alerts and correlation logs for diagnosis |

## Required deployment validation

The PR must remain draft until all items below are documented:

1. Build the backend in Release mode and run focused diagnostics/middleware tests.
2. Run frontend lint, TypeScript checking and production build.
3. Build Bicep and review Azure what-if output.
4. Confirm the role assignment is scoped only to the Log Analytics workspace.
5. Deploy configuration and allow RBAC propagation.
6. Verify the endpoint rejects non-Superadmin callers.
7. Execute each fixed KQL query against the production workspace and verify exact table/column behavior.
8. Generate one controlled frontend exception containing no customer data.
9. Generate one controlled backend exception in a safe internal flow.
10. Generate one controlled HTTP 5xx result without an unhandled exception and confirm it appears once.
11. Confirm weighted counts match a direct Azure query using `sum(ItemCount)`.
12. Confirm frontend startup/module errors captured before React render are flushed after telemetry initialization.
13. Stop or misconfigure one query in a safe environment and verify stale, partial and unavailable states.
14. Verify that malformed and Azure `PartialError` responses never display as zero or empty.
15. Confirm frontend and backend pipeline timestamps update after real traffic.
16. Confirm no token, e-mail, phone, GUID, payload, arbitrary correlation ID or stack trace appears in the API response or browser.
17. Validate loading, current, empty, partial, stale, unavailable, retry, truncation, filter and narrow-mobile states with Playwright.
18. Verify the dashboard error boundary cannot take down organization administration.

Do not generate destructive exceptions against real customer cases.

## Incident usage

Use the dashboard to identify:

- whether failures are frontend or backend;
- whether both telemetry pipelines have recently been observed;
- the affected route or operation;
- the active release;
- repeated error fingerprints;
- correlation IDs for deeper investigation;
- whether data is current, partial, stale or truncated.

Use Azure Monitor alerts for notification and Azure Portal/Application Insights for full authorized investigation. Follow the maintained incident and privacy-breach process where customer data or security may be affected.

## Rollback

Revert the application PR and infrastructure role/configuration additions. Removing the dashboard does not remove existing telemetry or Azure Monitor alerts. If the role assignment is removed separately, the dashboard shows `permission_denied` until the application change is also rolled back.
