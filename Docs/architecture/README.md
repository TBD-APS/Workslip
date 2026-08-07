# Architecture documentation

**Status:** Draft index  
**Owner:** Architecture owner  
**Review cadence:** When trust boundaries, persistence, authentication, deployment topology or major dataflows change

Current implementation is the technical source of truth. This area records durable architectural decisions and the small amount of system-level context that is expensive to rediscover from code.

## Accepted decisions

- [`adr/0001-managed-identity-runtime-and-secret-lifecycle.md`](adr/0001-managed-identity-runtime-and-secret-lifecycle.md)
- [`adr/0002-immediate-pwa-update-activation.md`](adr/0002-immediate-pwa-update-activation.md)
- [`adr/0003-github-infrastructure-oidc-bootstrap.md`](adr/0003-github-infrastructure-oidc-bootstrap.md)
- [`adr/0004-retire-maintained-repository-snapshots.md`](adr/0004-retire-maintained-repository-snapshots.md)

## Missing high-value views

These would add value when implemented because they summarize boundaries rather than duplicate code:

- `system-context.md` — users, external systems and trust boundaries.
- `containers.md` — frontend, API, SQL, Azure services and external integrations.
- `domain-and-dataflows.md` — tenant ownership and the core job/worksheet dataflows.

Do not create them merely to fill the list. Add each when there is enough stable information to make the page useful and maintainable.

## ADR states

An ADR is `Proposed`, `Accepted`, `Superseded` or `Rejected`. Only accepted decisions are normative, and implementation still must be checked when verifying current runtime behaviour.
