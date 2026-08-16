# MR SAAS'y Control Plane

This directory is the isolated Gate 0 bootstrap for the MR SAAS'y AI control plane.

It currently proves only two things:

1. the Laravel service can boot without Workslip/product database credentials or AI-provider credentials;
2. AI/provider dependency directions are machine-enforced before any real provider adapter is allowed to exist.

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

- direct DB/Eloquent/persistence symbol guard against AI/provider namespaces;
- a fixture proving the symbol guard actually rejects direct DB access;
- the real Deptrac graph;
- a legal dependency fixture that must pass;
- forbidden provider/application → persistence fixtures that must fail for the intended reason;
- Mermaid evidence generation to `build/architecture.mmd`.

No Deptrac baseline or skip list is accepted as part of Gate 0.

## Current scope

Gate 0 contains platform/provider contracts only. It deliberately does not implement:

- Kimi/OpenAI/Ollama adapters;
- Context/Policy Gateway behavior;
- product adapters;
- persistence;
- customer-facing AI endpoints;
- direct Workslip data access.

Those capabilities remain blocked until this gate is green and independently reviewed.
