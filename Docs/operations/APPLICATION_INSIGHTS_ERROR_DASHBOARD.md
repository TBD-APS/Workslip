# Application Insights error dashboard

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** `DiagnosticsEndpoints`, `ApplicationInsightsErrorDiagnosticsService`, Application Insights/Log Analytics configuration and the Superadmin diagnostics UI  
**Review cadence:** On telemetry, Azure RBAC, KQL schema or incident-process changes

## Purpose

The Superadmin error dashboard gives an operational view of recent Workslip frontend and backend failures without requiring Azure Portal access. It is read-only and complements, but does not replace, Azure Monitor alerts, Application Insights investigation or the incident process.

The dashboard is loaded only when a Superadmin opens it from the Superadmin page. Ordinary tenant users and normal application startup do not query Log Analytics.

## Data flow and trust boundary

1. The frontend records sanitized browser exceptions in Application Insights.
2. The API records structured Serilog traces and correlation identifiers in Application Insights.
3. The API managed identity obtains an Azure token for the Log Analytics query API.
4. The API executes fixed, version-controlled KQL against the configured workspace.
5. The API maps only allowlisted columns, sanitizes them again and groups repeated failures by a non-reversible fingerprint.
6. The browser receives only the safe dashboard contract.

The browser never receives Azure credentials, workspace access tokens, KQL, raw table rows, stack traces or complete custom dimensions.

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

The fixed queries use:

- `AppExceptions` for frontend exceptions emitted by Workslip's browser telemetry;
- `AppTraces` with error-or-higher severity for backend Serilog traces.

The summary always counts the last hour, 24 hours and seven days. Detail rows are bounded before mapping and then grouped by sanitized fingerprint.

## Returned fields

The API contract may contain only:

- UTC timestamp;
- source (`frontend` or `backend`);
- normalized severity;
- sanitized error type;
- non-reversible fingerprint;
- sanitized message;
- normalized route or operation;
- release identifier;
- safe correlation ID or trace ID;
- grouped occurrence count.

It must never return raw exception objects, stack traces, request or response bodies, headers, authorization values, cookies, e-mail addresses, phone numbers, tenant IDs, entity IDs or complete Application Insights properties.

## Redaction

Redaction is performed in two places:

1. Frontend telemetry sanitizes browser exceptions before ingestion.
2. The diagnostics API sanitizes every returned field regardless of source.

The API removes or normalizes:

- bearer/basic credentials and common secret keys;
- token query parameters and long token-like values;
- e-mail addresses and phone numbers;
- GUID and numeric route segments;
- line breaks and excessive length;
- unsafe correlation identifiers.

Backend detail queries prefer the Serilog message template over the rendered message to reduce the risk of returning structured property values. Redaction is still mandatory after query parsing.

## Availability states

The dashboard returns a safe unavailable state rather than exposing Azure response content:

| Reason | Meaning | Operator action |
|---|---|---|
| `not_configured` | Workspace ID is missing | Verify App Configuration deployment and API refresh |
| `permission_denied` | Managed identity lacks query access | Verify the workspace-scoped role assignment and propagation |
| `throttled` | Azure returned a rate-limit response | Wait and retry; inspect query frequency if recurring |
| `timeout` | Token or query operation timed out | Check Azure health and workspace response time |
| `query_failed` | Other safe query failure | Use Azure Portal and correlation logs for deeper diagnosis |

Raw Azure error bodies must not be logged or returned by this feature.

## Deployment validation

Before marking the change ready:

1. Build Bicep and review Azure what-if output.
2. Confirm the role assignment is scoped only to the Log Analytics workspace.
3. Deploy configuration and allow RBAC propagation.
4. Verify the endpoint rejects non-Superadmin callers.
5. Generate one controlled frontend exception containing no customer data.
6. Generate one controlled backend exception in a safe internal flow.
7. Confirm both appear with source, release/operation and correlation metadata.
8. Confirm no token, e-mail, phone, GUID, payload or stack trace appears in the API response or browser.
9. Validate loading, empty, unavailable, retry, filters and narrow-mobile layout with Playwright.

Do not generate destructive exceptions against real customer cases.

## Incident usage

Use the dashboard to identify:

- whether failures are frontend or backend;
- the affected route or operation;
- the active release;
- repeated error fingerprints;
- correlation IDs for deeper investigation.

Use Azure Monitor alerts for notification and Azure Portal/Application Insights for full authorized investigation. Follow the maintained incident and privacy-breach process where customer data or security may be affected.

## Rollback

Revert the application PR and infrastructure role/configuration additions. Removing the dashboard does not remove existing telemetry or Azure Monitor alerts. If the role assignment is removed separately, the dashboard will show `permission_denied` until the application change is also rolled back.
