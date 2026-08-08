# Workslip documentation instructions

Root [`AGENTS.md`](../AGENTS.md) applies. This file adds rules for maintained documentation under `Docs/`.

## Write one kind of truth at a time

Every maintained statement should be clearly one of:

- **Current fact** — verified against code, configuration, runtime evidence or an authoritative external source.
- **Decision** — an accepted design or policy, preferably recorded in an ADR when significant.
- **Plan** — proposed or pending work; never written as implemented behaviour.
- **Historical context** — retained for explanation and visibly marked historical.

Do not mix these categories in wording that makes a plan look deployed or an old decision look current.

## Keep docs close to their authority

- Technical behaviour: verify against current code/config/tests and link to the stable concept, not a copied implementation dump.
- API behaviour: endpoint source and runtime OpenAPI are authoritative; Postman is verification material.
- Architecture: use accepted ADRs for non-obvious durable decisions.
- Delivery status: Linear is authoritative; maintained docs should describe current state rather than depend on a future issue transition.
- Legal/compliance claims: use the compliance baseline and current official sources; do not turn engineering evidence into a blanket compliance claim.

Prefer updating an existing maintained page. Avoid a second document that repeats the same rule or runtime description.

## Document states

Use the states defined in [`README.md`](README.md): Active, Draft, Historical and Generated.

If a page is no longer authoritative but existing links matter, reduce it to a short historical/superseded pointer instead of keeping duplicate active guidance alive.

## What belongs in maintained docs

Document behaviour that is expensive to rediscover: system boundaries, operational procedures, public/API contracts, non-obvious security/privacy decisions, deployment assumptions and accepted architectural trade-offs.

Do not document line-by-line implementation detail that can be read directly from code and is likely to drift.

## Validation

For documentation changes:

```bash
python tools/docs/check_docs.py
```

Also verify any technical claim against its primary repository source. Use [`agents/VALIDATION.md`](agents/VALIDATION.md) when the documentation change accompanies implementation work, and the compliance baseline when the change makes personal-data, legal or AI claims.
