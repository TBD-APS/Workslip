# ADR 0010: Bootstrap the MR SAAS'y AI control plane as an isolated extractable service

- Status: Accepted
- Date: 2026-08-16
- Tracking: WOR-573, WOR-579, WOR-582

## Context

MR SAAS'y needs a provider-neutral AI control plane before Kimi, OpenAI, Ollama or future providers can receive product context. The first security requirement is architectural rather than provider-specific: model/provider code must not acquire a direct path to Workslip persistence or product-domain data.

No dedicated MR SAAS'y repository currently exists in the connected GitHub account, while Workslip is the current reference repository for Control Center/platform extraction work. Putting provider code directly inside Workslip backend layers would create the coupling the control plane is intended to prevent.

## Decision

Bootstrap the first Laravel control-plane service under:

```text
platform/mr-saasy-control-plane/
```

inside the current Workslip reference repository.

This placement is temporary infrastructure ownership, not a Workslip domain boundary. The service must remain independently bootable and extractable to a future dedicated MR SAAS'y repository without changing platform/provider contracts.

Gate 0 mechanically enforces this direction:

```text
Product Adapter -> Context/Policy -> Agent Application -> Provider Contract -> Provider Adapter

Provider Adapter -X-> Persistence
Agent Application -X-> Persistence
Provider/Agent -X-> DB/Eloquent/query APIs
Provider/Agent -X-> Workslip domain/repositories
```

Deptrac is the primary dependency gate. A separate deterministic source guard rejects direct DB/Eloquent/platform-persistence and Workslip-adapter symbols from AI/provider namespaces. CI also contains intentional violation fixtures so the gate proves that forbidden dependencies fail rather than merely reporting that current code happens to contain none.

The Gate 0 service contains contracts/bootstrap only. Real provider adapters, Context/Policy implementation, product adapters and persistence are separate later issues and remain blocked until Gate 0 is green and independently reviewed.

## Repository boundary

The Laravel service must not reference:

- Workslip backend/frontend projects;
- Workslip domain entities, repositories, DbContext/schema or DTOs;
- Workslip database credentials;
- generic SQL/query/repository ports that could bypass product adapters.

Product-specific integration may later exist only behind narrow `ProductAdapters/Contracts` and the Context/Policy boundary.

No Kimi/OpenAI/Ollama key is required for Gate 0.

## Consequences

### Positive

- the trust boundary is executable before provider code exists;
- Workslip remains a consumer/product adapter rather than becoming the AI platform core;
- future repository extraction is a source move rather than a domain redesign;
- CI can prove both allowed and forbidden dependency directions;
- Control Center can later surface architecture evidence generated from the same rules used by CI.

### Costs

- the repository temporarily hosts a second application/runtime;
- CI must provision PHP/Composer only when control-plane paths change;
- a later repository extraction still requires operational migration of CI/secrets/deployment ownership even though contracts remain stable.

## Non-goals

- implementing an AI provider;
- exposing customer-facing AI functionality;
- giving the control plane direct Workslip database access;
- replacing Workslip application/domain logic;
- deciding the final standalone repository/deployment topology before the boundary is proven.
