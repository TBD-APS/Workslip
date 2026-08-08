# Architecture documentation

**Status:** Active index  
**Owner:** Architecture owner  
**Review cadence:** When trust boundaries, persistence, authentication, deployment topology or major dataflows change

Current implementation is the technical source of truth. This area records durable decisions and the small amount of system context that is expensive to rediscover from code.

## Current architecture views

- [`domain-and-dataflows.md`](domain-and-dataflows.md) — tenant ownership and core data-integrity boundaries.

Useful future views, when they can be kept concise and stable:

- `system-context.md` — users, external systems and trust boundaries.
- `containers.md` — frontend, API, SQL, Azure services and external integrations.

Do not create a page merely to fill the list. Add it only when it reduces rediscovery cost without copying implementation detail.

## Accepted decisions

- [`adr/0001-managed-identity-runtime-and-secret-lifecycle.md`](adr/0001-managed-identity-runtime-and-secret-lifecycle.md)
- [`adr/0002-immediate-pwa-update-activation.md`](adr/0002-immediate-pwa-update-activation.md)
- [`adr/0003-github-infrastructure-oidc-bootstrap.md`](adr/0003-github-infrastructure-oidc-bootstrap.md)
- [`adr/0003-vapid-key-rotation-and-subscription-repair.md`](adr/0003-vapid-key-rotation-and-subscription-repair.md)
- [`adr/0004-retire-maintained-repository-snapshots.md`](adr/0004-retire-maintained-repository-snapshots.md)
- [`adr/0005-main-as-production-boundary.md`](adr/0005-main-as-production-boundary.md)

Two historical accepted ADRs already use number `0003`. Keep their filenames stable so existing links do not break; allocate the next new ADR number after the highest existing number and do not create another duplicate.

## ADR states

An ADR is `Proposed`, `Accepted`, `Superseded` or `Rejected`. Accepted ADRs are normative decisions; inspect implementation/configuration when verifying current runtime behaviour.
