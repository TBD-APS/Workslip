# Architecture documentation

**State:** Draft  
**Owner:** Architecture owner (assign in Linear)  
**Review cadence:** Monthly and whenever trust boundaries, persistence, authentication, deployment topology or major dataflows change.

This area is the maintained home for Workslip architecture and Architecture Decision Records (ADRs).

The implementation is the source of truth. Until WOR-142 is completed, use the backend/frontend source, infrastructure definitions and current workflows instead of assuming that dated plans describe deployed behaviour.

## Planned maintained documents

- `system-context.md` — users, external systems and trust boundaries.
- `containers.md` — frontend, API, SQL, Azure services and external integrations.
- `domain-and-dataflows.md` — organization/tenant ownership, jobs, worksheets and central flows.
- `adr/` — accepted technical decisions with context, alternatives and consequences.

## Accepted ADRs

- [`ADR 0001: Azure SQL managed identity and App Service IP allowlist`](adr/0001-azure-sql-managed-identity-and-network-allowlist.md)

## ADR state

An ADR may be `Proposed`, `Accepted`, `Superseded` or `Rejected`. A proposed ADR is not evidence that its behaviour is implemented.
