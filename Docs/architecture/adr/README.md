# Architecture Decision Records

**Owner:** Workslip engineering  
**Review cadence:** Review affected ADRs in every PR that changes the recorded decision

ADRs record stable technical decisions and unresolved decisions that must not be inferred from plans.

## Status meanings

- `Proposed` — recommendation awaiting explicit approval. It is not evidence of implemented behaviour.
- `Accepted` — approved or clearly embodied by the maintained implementation.
- `Superseded` — replaced by a newer ADR.
- `Rejected` — considered and deliberately not chosen.

## Index

| ADR | Status | Subject |
|---|---|---|
| [0001](0001-job-status-transitions.md) | Proposed | Job status transition matrix and authorization boundary |
| [0002](0002-inventory-posting-and-idempotency.md) | Proposed | Future inventory posting, transactions and idempotency |
| [0003](0003-schema-change-and-deployment.md) | Accepted | Current startup-owned schema migration and deployment model |
| [0004](0004-offline-draft-and-pwa-updates.md) | Proposed | Safe local drafts, online submit and controlled PWA updates |

## Template

```markdown
# ADR NNNN: Title

**Status:** Proposed | Accepted | Superseded | Rejected  
**Date:** YYYY-MM-DD  
**Owners:** ...  
**Related:** Linear issue / PR

## Context

What is true and what problem requires a decision?

## Decision

What is chosen? For a proposed ADR, state that it is not implemented.

## Consequences

Positive and negative consequences.

## Alternatives considered

Other credible options and why they were not selected.

## Verification

How the decision is proven in code, tests, runtime or operations.
```
