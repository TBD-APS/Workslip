# ADR 0009: Platform Control Center uses a normalized read model

- Status: Accepted
- Date: 2026-08-15
- Tracking: WOR-552, WOR-553

## Context

Workslip already produces operational evidence through GitHub Actions, health/readiness endpoints, Application Insights and Linear delivery/agent state. These systems are separate sources of truth with different payloads and lifecycle semantics.

MR SAAS'y needs one place to answer what is running, failing, stale, blocked, deployed and healthy across products. Copying provider payloads or product business data into a universal platform schema would create provider coupling, duplicate sensitive data and make the platform the accidental owner of product semantics.

## Decision

The Platform Control Center is a read-oriented projection over provider-neutral contracts.

The durable platform model consists of:

- application + environment identity;
- normalized health/automation state;
- observation and freshness timestamps;
- exact revision/issue/PR/run identifiers when available;
- evidence references back to the owning source.

Provider adapters own GitHub, Azure/Application Insights, Linear and product-specific payload mapping. Raw CI logs, traces, exception payloads, secrets and customer PII do not cross the adapter boundary by default.

`UNKNOWN`, `BLOCKED` and `STALE` are first-class states and must never be coerced to healthy/successful.

The first implementation is hosted in the Workslip repository as the reference consumer while MR SAAS'y platform extraction is still in progress. The contract itself must not reference Workslip domain DTOs, routes or business concepts. Workslip-specific registration and adapters remain replaceable edges.

The v1 surface is read-only. Re-run, merge, deploy, rollback or recovery actions remain in their owning systems until read-model provenance and authorization have been proven separately.

## Consequences

### Positive

- one UI/read API can combine multiple providers without adopting their schemas;
- exact evidence remains traceable to source systems;
- products can keep business KPIs and sensitive domain data local;
- second products/providers can be added through adapters rather than control-center core changes;
- degraded or unavailable evidence fails visibly instead of becoming false green state.

### Costs

- the read model is eventually consistent;
- adapters need explicit mapping and freshness semantics;
- the same real-world event may have several evidence references across CI, release and runtime systems;
- initial Workslip-hosted code must later be extracted without allowing Workslip-specific assumptions into the contract.

## Non-goals

- a universal cross-product business warehouse;
- replacement of GitHub Actions, Linear, Application Insights or Power BI;
- raw log aggregation inside the platform domain model;
- automatic production control from the v1 dashboard.
