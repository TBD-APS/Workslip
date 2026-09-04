# ADR 0018 — Azure Monitor is the telemetry boundary

**Status:** Accepted

**Owner:** Workslip architecture owner

**Decision scope:** How Workslip's telemetry and logs reach a platform consumer. It does not specify what MR SAAS'y builds on top of the query surface, its dashboards, alerting or retention of derived data — those are the platform's.

## Context

MR SAAS'y needs Workslip's telemetry and logs. The question was never whether, but through which seam.

Workslip previously pushed a curated slice outward: `MrSaasyBugRadarCheckpointPublisher` posted sanitized error fingerprints to an MR SAAS'y activity endpoint. That coupled the product to the consumer — a base URL, a rotating activity token, a Cloudflare Access service identity, a retry interval — and produced a failure mode where the consumer being unreachable looked like a Workslip fault. [ADR 0017](0017-ai-retrieval-belongs-to-mr-saasy-agent-runtime.md) records the same boundary for retrieval; this one completes it for telemetry.

The replacement is not a Workslip API. `/api/admin/diagnostics` is a curated, rate-limited, Superadmin-gated view built for a dashboard screen. Telemetry is high-volume and high-cardinality, and an application endpoint is the wrong shape for it.

The correct seam already exists and is already wired. The API emits through `Microsoft.ApplicationInsights.AspNetCore` and `Serilog.Sinks.ApplicationInsights`; the Container Apps environment ships container stdout/stderr through `appLogsConfiguration`; both land in the same Log Analytics workspace, and the Application Insights component is workspace-based (`WorkspaceResourceId`). Everything a consumer could want is already in one place.

## Decision

1. **Azure Monitor holds the data. Consumers read from it.** Workslip emits telemetry and logs to Application Insights and Log Analytics and does not deliver them anywhere else. MR SAAS'y queries the Log Analytics workspace with KQL through the Azure Monitor Query API.

2. **Workslip holds no consumer identity.** No platform address, credential, token or transport configuration lives in the product. Access is granted the other way round: the consumer's own managed identity receives a read-only Azure role (`Log Analytics Reader` or `Monitoring Reader`) scoped to the workspace. That grant is revocable and auditable without a Workslip deployment.

3. **Cross-tenant access uses Azure Lighthouse, not a shared secret.** The `live` tenant migration means the consumer may sit in a different tenant. Delegated cross-tenant reading is what Lighthouse exists for; the alternative — a service principal with a client secret in the Workslip tenant — reintroduces exactly the credential this ADR removes.

4. **Workslip's only obligation is to emit well.** Stable role names per service and environment, a tenant/organization dimension so a consumer can slice per customer, correlation identifiers through the chain, consistent severity, and no personal data in log messages. Emitting correctly is a product responsibility; consuming is not.

## Preconditions that are not met today

The seam works. The configuration around it does not yet support "Azure holds all the data", and each of these fails quietly rather than loudly.

**The workspace caps ingestion at 1 GB per day.** `main.bicep` sets `workspaceCapping.dailyQuotaGb: 1`, commented as the lowest cap Azure allows. When a daily cap is reached, Log Analytics stops ingesting until the next day. A consumer would see telemetry simply end mid-afternoon, with no error anywhere, and the gap is unrecoverable — data not ingested is not stored late. Raising or removing the cap is a deliberate cost decision and is not made here.

**Production retention is implicit.** The production workspace sets no `retentionInDays`, so it takes the Azure default of 30 days; the demo workspace sets 30 explicitly. Whatever the right number is, production retention should be stated rather than inherited, because it is the hard limit on how far back any consumer can look.

**Telemetry carries no tenant dimension.** `CorrelationTelemetryInitializer` sets `CorrelationId` and nothing else. A consumer can follow one request chain but cannot answer "which customer is affected" without joining against data it does not have. A platform control centre for a multi-tenant product needs that dimension at the source.

## Consequences

- The consumer gets everything — requests, dependencies, exceptions, traces, custom metrics and container logs — rather than the curated subset the old bridge chose in advance.
- Onboarding a second product is the same mechanism against another workspace, not another bespoke bridge.
- Workslip's telemetry code stays correct whether or not anyone is reading it, and a consumer outage is invisible to the product.
- A consumer that stores Workslip logs becomes a processor of whatever those logs contain. Keeping personal data out of log messages is therefore a boundary condition, not hygiene; see the [compliance baseline](../../compliance/GDPR_AI_ACT_BASELINE.md).
- The three preconditions above are real work with a cost and a compliance conversation attached. Until the daily cap in particular is addressed, the decision holds but the guarantee does not.
