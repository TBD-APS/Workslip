# Application Insights error dashboard

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** `DiagnosticsEndpoints`, `ApplicationInsightsErrorDiagnosticsService`, frontend telemetry bootstrap, Azure monitoring configuration, the Superadmin diagnostics UI and `supportSnapshot.ts`  
**Review cadence:** On telemetry, Azure RBAC, diagnostics contract, support-export or incident-process changes

## Purpose

The Superadmin error dashboard gives a read-only operational view of recent Workslip frontend and backend failures without requiring Azure Portal access. It complements Azure Monitor/Application Insights investigation; it is not the authoritative telemetry store or alerting system.

## Security boundary

`GET /api/admin/diagnostics/errors` is protected by the Superadmin authorization policy and the `diagnostics-read` rate limiter. The response is `Cache-Control: no-store`.

The API, not the browser, queries the configured Log Analytics workspace. Azure credentials, workspace access tokens, KQL, raw telemetry rows, stack traces, request/response bodies, headers and arbitrary custom dimensions must not be returned to the browser.

The API managed identity should have only the workspace-scoped read access required by the current infrastructure definition. `Azure:ApplicationInsights:WorkspaceId` is configuration, not a secret. Do not replace the managed-identity boundary with a workspace key or broad administrator credential.

## Trust invariant

The dashboard distinguishes **current**, **partial**, **stale** and **unavailable** data. Failed or malformed Azure responses must never be presented as a trustworthy zero or empty list.

A complete result requires the current diagnostics service contract to consider all required query sections valid and non-partial. When a complete prior snapshot is available, a later query failure may be shown as stale according to the service's current cache policy. The running service/tests own exact retry, timeout, grouping and cache durations; do not copy those implementation constants into this runbook.

Frontend/backend telemetry last-seen timestamps are health signals only. Missing or old telemetry does not prove there were no errors.

## Diagnostics contract

The current endpoint accepts only the allowlisted range/source/limit values defined by `ApplicationInsightsErrorDiagnosticsService`; it does not accept client-supplied KQL.

Returned data is deliberately reduced to operational metadata such as:

- availability/completeness/staleness/truncation state;
- generation/data timestamps and summary counts;
- frontend/backend telemetry health timestamps;
- sanitized source, severity, error type and message;
- stable non-reversible grouping fingerprint;
- normalized route/operation/release context;
- safe correlation/trace identifiers when they satisfy the current output policy;
- grouped occurrence/context counts.

The contract must not expose raw exception objects, stack traces, payloads, headers, authorization values, cookies, e-mail addresses, phone numbers, tenant/entity identifiers or complete telemetry properties.

Exact query tables, grouping logic, response-schema validation and retry behavior are implementation details owned by the diagnostics service and its focused tests. Change this document only when an operator/security invariant changes.

## Support snapshot

The Superadmin UI can copy a versioned, allowlisted diagnostics snapshot to the clipboard after a validated response exists. The copy action does not itself transmit data to ChatGPT or another service.

The snapshot must preserve stale/partial/unavailable state and exclude unexpected runtime object properties by default. Clipboard failure must not fall back to persistent storage, download or network transmission.

A copied snapshot is still operational data. Do not paste it into public issues or unapproved external systems.

## Incident usage

Use the dashboard to answer:

- is the observed failure frontend or backend;
- are telemetry pipelines being observed recently;
- is an error recurring and over what observed interval;
- which sanitized route/operation/release context is represented;
- which correlation identifier can be used for deeper authorized investigation;
- is the dashboard current, partial, stale or truncated.

Use Azure Portal/Application Insights for full authorized telemetry investigation and Azure Monitor for alerting. Follow the maintained incident/privacy process when customer data or security may be affected.

## Validation when this area changes

Follow [`../agents/VALIDATION.md`](../agents/VALIDATION.md) and validate the risk that changed. Diagnostics changes normally require:

- backend Release build and focused authorization/query/redaction tests;
- frontend tests/build for changed dashboard or support-export behavior;
- infrastructure validation when workspace/RBAC/configuration changes;
- safe HTTP/browser validation for authorization, current/partial/stale/unavailable states when user-visible behavior changes;
- inspection that sensitive data is not introduced into API responses or retained browser evidence.

Do not generate destructive failures against real customer cases merely to prove telemetry.
