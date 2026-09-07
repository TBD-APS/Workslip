# ADR 0020 — Azure Monitor is the telemetry boundary

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

## Preconditions

The seam works. Three things around it did not, and each failed quietly rather
than loudly. All three are addressed in the same change that recorded this
decision.

**Ingestion cap.** `main.bicep` capped the production workspace at
`dailyQuotaGb: 1`, commented as the lowest cap Azure allows. A reached daily cap
stops ingestion until the next day, so a consumer would have seen telemetry end
mid-afternoon with no error anywhere, and the gap is unrecoverable — data not
ingested is not stored late.

The cap is now 2 GB/day, derived from the cost budget rather than picked. It
doubles the old cap, so it stops discarding signal unnoticed, while claiming
about 60 GB/month against the roughly 266 of headroom `budgets.bicep` leaves
above its baseline — near half of it rather than all. The always-warm replica
in `aca/app.bicep` draws on the same headroom, so the two are read
together. It stays capped rather than uncapped, because an uncapped workspace has
no brake at all, and the per-GB rate behind the currency estimate was not
verified for this region: the GB figure is the real control. Measure actual daily
ingestion before moving it again.

**Retention.** The production workspace set no `retentionInDays` and inherited
the Azure default, while the demo workspace stated its own. It is now stated
explicitly as 30 days. The number is unchanged; what changed is that it is
visible in the template, because it is the hard limit on how far back any
consumer can query and should not be discovered by experiment.

**Tenant dimension.** `CorrelationTelemetryInitializer` set only `CorrelationId`,
so a consumer could follow one request chain but could not answer which customer
was affected. It now also emits `OrganizationId`, read from the same
`organizationId` claim `CurrentUserContext` uses, as a global property so it
lands on traces, dependencies and exceptions rather than only on requests.

Two details of that are deliberate. The dimension is the organization and not the
user: it identifies a tenant rather than a person, so it does not turn every
trace into personal data. And only a well-formed identifier is emitted, so a
malformed claim cannot inject arbitrary text into a dimension the platform groups
by. The claim name is duplicated rather than shared with `CurrentUserContext`
because this initializer is a singleton and `ICurrentUserContext` is scoped;
injecting the scoped service would be a captive dependency.

## Consequences

- The consumer gets everything — requests, dependencies, exceptions, traces, custom metrics and container logs — rather than the curated subset the old bridge chose in advance.
- Onboarding a second product is the same mechanism against another workspace, not another bespoke bridge.
- Workslip's telemetry code stays correct whether or not anyone is reading it, and a consumer outage is invisible to the product.
- A consumer that stores Workslip logs becomes a processor of whatever those logs contain. Keeping personal data out of log messages is therefore a boundary condition, not hygiene; see the [compliance baseline](../../compliance/GDPR_AI_ACT_BASELINE.md).
- Telemetry now costs more than it did. The old 1 GB/day cap kept spend near zero by discarding signal; a cap that leaves room for the full signal means the workspace can bill for it. That is the trade this decision makes, and the daily cap is the control for it.
- Nothing here grants the consumer anything. The role assignment on the workspace, and the Lighthouse delegation if the consumer sits in another tenant, are separate steps outside this repository. Until they exist, Workslip emits correctly and no one is reading.
