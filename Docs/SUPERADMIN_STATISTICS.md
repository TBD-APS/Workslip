# SuperAdmin Statistik

**Status:** Active  
**Owner:** Workslip maintainers  
**Last verified:** 2026-09-30  
**Implementation reference:** PR #1083 / WOR-659

## Current behaviour

SuperAdmin has a dedicated statistics page at `/superadmin/statistik`. The page reads from `/api/superadmin/analytics/workflow-statistics` and uses the persistent Workslip database as its source of truth.

The dashboard automatically refreshes every 60 seconds through `refetchInterval: 60_000`. A manual refresh action is also available. The 60-second cadence is the maintained behaviour for this surface; it is near-real-time polling rather than server push.

Frontend query cache and component state are presentation mechanisms only. Local storage, mock data and temporary React state are not authoritative statistics sources.

## Data platform and persistence

Workslip backend uses SQL Server through Entity Framework Core. Runtime database connectivity is resolved from `Azure:Sql:ConnectionString`.

The live environment is configured with:

- Azure resource group: `rg-mrsoftwarev2-live`
- Azure SQL server: `db-mrsoftwarev2-live-server`
- Azure SQL database: `db-mrsoftwarev2-live`
- Azure region: `swedencentral`

There is no separate statistics database. The statistics endpoint derives its response from the existing Workslip relational data and audit events.

## Active employee time

Active completion time is recorded from the employee workflow as short segments. The frontend flushes active time approximately every 10 seconds and on relevant lifecycle boundaries such as blur, hidden state and idle transitions.

Each segment receives a client-generated `segmentId`. Backend persistence uses that identifier as the `JobEvents` event ID. A retry of the same segment is therefore idempotent and does not count the same duration twice. Reuse of an existing segment ID for another organization, job or event type is rejected as a conflict.

The event type is `analytics.employee_active_segment`. Analytics transmission is best-effort and must not block the core job workflow if telemetry delivery fails.

## Rejection categories

The admin rejection flow stores a structured `rejectionCategory` in the existing `JobEvents` audit event in the same database transaction as the status change. The human-facing manager comment remains in `RejectionNote`, keeping internal analytics metadata separate from the text shown to the employee.

Statistics prefer the structured category when present. Historical jobs without structured rejection metadata use the existing free-text classifier as a fallback, so historical rejection data remains visible while new data has a more reliable classification source.

Maintained categories include missing required data, incorrect measurement/value, missing photo documentation, wrong control point, incomplete work, duplicate/wrong job, system/UI signal, other and unclassified.

## Current validation contract

Validation follows [`agents/VALIDATION.md`](agents/VALIDATION.md): persisted multi-endpoint behaviour is verified through API/integration coverage, and the changed user-visible critical journey is verified through Playwright.

The release evidence for this feature includes:

- backend restore, Release build and tests
- frontend unit/regression tests
- frontend production build and API-contract validation
- lint-debt gate
- contracts + documentation validation
- Postman integration against a disposable SQL Server
- authenticated Playwright against a disposable SQL Server and the real backend
- Repository Data Hygiene
- the aggregate CI Gate

The browser scenario verifies the statistics endpoint returns HTTP 200, structured rejection data is read back from persistence, the statistics UI renders without its error state, KPI cards are present, and the layout works at desktop 1280 and mobile 390 without horizontal overflow, page errors, console errors or failed API responses.

The wider authenticated Playwright suite also exercises submit → reject → correct → resubmit → approve, together with the other critical job lifecycle scenarios.

## Historical context: stabilization journey

The native Statistics page superseded the older productivity/analytics block on the SuperAdmin home surface. During stabilization, the old block was removed so there is one maintained SuperAdmin statistics experience.

The data model was then strengthened in two areas. Rejection analysis moved from free-text-only inference to structured audit metadata with historical fallback. Active-time events gained idempotent client-generated segment IDs and a shorter flush interval so retries cannot over-report active duration.

The first browser-evidence run for the stabilized page failed although the statistics API itself was returning successfully. The failure was in the Playwright scenario: it waited for the retired selector `.superadmin-statistics-page`, while the current UI root is `.statistics-page`. The scenario was corrected to the current selectors and expanded to validate the actual SuperAdmin error state, KPI cards, API response and responsive layout.

On product-code head `b84c83530aefafc80d0c59f5288c62d2536ee393`, CI run `36707840716` completed successfully. That run included 877/877 backend tests and 500/500 frontend tests, plus successful Postman, Playwright, build, contract, lint and CI Gate jobs. Repository Data Hygiene run `36707840672` also completed successfully.

Historical exact-head results are evidence for that exact commit only. If the PR head changes, the required checks must complete again on the new head before merge.

## 60-second refresh semantics

`refetchInterval: 60_000` means database changes become visible when the statistics query next polls successfully, without requiring a page reload. This is not SignalR or WebSocket push and should not be described as instantaneous real-time delivery.

The 60-second polling design is intentionally simpler than a continuous event stream and limits avoidable load for an administrative analytics surface.

## Data growth and scaling boundary

The frontend consumes aggregated statistics results instead of loading the database into client state. As data volume grows, the same API contract can continue to serve the page while backend/database implementation is optimized independently.

If measured workload later shows that statistics queries are too expensive, optimization belongs at the backend/data layer, for example through indexes, tighter projections, pre-aggregation or caching. Such optimizations require their own measurement and validation; this document does not claim that the current query implementation is unbounded in scale.

## Release boundary

Green feature-branch CI is merge evidence, not production-deployment evidence. Production is only updated after the change is merged through the repository's release process and the relevant production deployment completes successfully.
