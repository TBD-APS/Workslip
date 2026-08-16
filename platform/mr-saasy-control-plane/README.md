# MR SAAS'y Control Plane

This directory is the isolated MR SAAS'y AI control plane foundation.

Gate 0 proves two security properties before any real provider adapter is allowed to exist:

1. the Laravel service can boot without Workslip/product database credentials or AI-provider credentials;
2. AI/provider dependency directions are machine-enforced.

Provider-neutral operating layers build on that boundary:

- [`docs/agent-routing.md`](docs/agent-routing.md) — role/model routing, capabilities, fallback and review separation;
- [`docs/executive-leadership.md`](docs/executive-leadership.md) — Founder/CEO hierarchy, functional delegation and human approval boundaries.

It is **not** a Workslip domain module and must remain extractable to a dedicated platform repository without changing its core contracts.

## Trust direction

```text
Product Adapter -> Context/Policy -> Agent Application -> Provider Contract -> Provider Adapter

Provider Adapter -X-> Persistence
Agent Application -X-> Persistence
Provider/Agent -X-> Laravel DB/Eloquent/query APIs
Provider/Agent -X-> Workslip domain/repositories
```

## Gate 0 commands

After `composer install`:

```bash
composer validate --strict
php artisan test
composer architecture
```

`composer architecture` runs:

- direct Workslip source-coupling guard;
- direct DB/Eloquent/persistence symbol guard against AI/provider namespaces;
- a fixture proving the symbol guard actually rejects direct DB access;
- the real Deptrac graph;
- a legal dependency fixture that must pass;
- forbidden provider/application → persistence fixtures that must fail for the intended reason;
- Mermaid evidence generation to `build/architecture.mmd`.

No Deptrac baseline or skip list is accepted as part of Gate 0.

## Current scope

The control plane currently contains:

- platform/provider contracts;
- role registry and configuration-driven primary/fallback model routing;
- capability/tool requirement validation;
- run provenance + separation-of-duties policy;
- Founder/CEO/functional-executive hierarchy and delegation rules;
- explicit human approval gates for public/high-impact/irreversible actions;
- explicit prohibition on executive self-escalation of permissions, budgets or governance.

It deliberately does not implement:

- Kimi/OpenAI/Ollama adapters;
- Context/Policy Gateway behavior;
- product adapters;
- persistence;
- customer-facing AI endpoints;
- autonomous executive writes/approvals;
- direct Workslip data access.

Real provider integrations remain blocked by Gate 0 review and their owning issues.
