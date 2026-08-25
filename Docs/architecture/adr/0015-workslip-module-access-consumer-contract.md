# ADR 0015 — Workslip module-access consumer contract

**Status:** Accepted

**Owner:** Workslip architecture owner

**Decision scope:** How Workslip *consumes* per-tenant module entitlement and gates capabilities. It does not define the platform's entitlement store, pricing, or the customer packaging model — those live in MR SAAS'y and the [modular product blueprint](../workslip-modular-product-blueprint.md).

## Context

Workslip needs to turn customer-visible modules on and off per tenant. The control plane for entitlement already exists in MR SAAS'y (a tenant-aware module-entitlement resolver, availability states, an access guard and a navigation host composer), and the platform boundary is already fixed by [ADR 0010](0010-mr-saasy-control-plane-bootstrap-boundary.md), [ADR 0013](0013-mr-saasy-spacecenter-privileged-admin-surface.md) and [ADR 0014](0014-mr-saasy-delivery-lifecycle-orchestration-boundary.md): Workslip is the first *consumer* of platform contracts, never the owner, and there is no shared database or cross-repository domain import.

Today Workslip has no entitlement or module catalog. Gating is purely role-based ([`permissions.ts`](../../../src/FE/src/providers/permissions/permissions.ts) and the backend auth policies), and the only flag abstraction is the client-side help wizard. We therefore need a small, Workslip-owned consumer contract that packaging, navigation and enforcement can all build on — without leaking platform identifiers into the product domain.

## Decision

1. **A Workslip-owned module vocabulary.** Customer-visible modules are named by stable, product-facing keys (`foundation`, `work-management`, `time-economics`, `compliance-evidence`, `field-collaboration`, `insights-exports`). The platform's opaque `ModuleId` values are mapped onto these keys **inside a product-owned adapter only**; no other code depends on platform identity. `foundation` is always-on and is never sold or disabled.

2. **Effective access is an intersection.** For every request:

   ```text
   tenant entitlement  ∩  release state  ∩  user role/permission  ∩  tenant/data scope
   ```

   These are distinct controls. Entitlement (this contract) is the tenant's contractual right to a module; it is never the same thing as the role/permission check, and a release flag can never grant a module the tenant is not entitled to.

3. **The server is the authority.** [`IWorkslipModuleAccess`](../../../src/BE/WorkslipApi/Workslip.Application/ModuleAccess/IWorkslipModuleAccess.cs) is the backend gate that sits alongside role authorization. Every protected endpoint, background worker, file operation and export enforces the module decision server-side. Frontend gating is convenience only.

4. **The frontend gates UX with `<FeatureGate>`.** [`FeatureGate`](../../../src/FE/src/providers/moduleAccess/FeatureGate.tsx) mirrors `<Can>`: `Can` answers "may this user act?", `FeatureGate` answers "is this module entitled for the tenant?". Capabilities that need both wrap both. Navigation and create-actions show only enabled, role-authorized capabilities. The frontend reads a read-only effective-capability summary; it never infers entitlement from hidden navigation.

5. **A product-owned adapter is the only platform seam.** The adapter consumes the MR SAAS'y module-entitlement contract over a versioned transport, pinning the host contract version, and projects the result into a **local** Workslip entitlement view. It maps platform `TenantId` to a Workslip `Organization` via an opaque external reference — the platform tenant identity is never a Workslip organization key.

6. **Fail closed, but never black out.** An unresolved module fails closed (only an explicit `Enabled` authorizes). To keep a platform outage from disabling the whole product, the adapter serves a last-known-good cache with a TTL; hard fail-closed applies only to a genuinely unknown tenant/module.

## Scope of this change

This ADR lands the **consumer contract and seams only**, with behaviour unchanged:

- Backend: `WorkslipModuleKey`, `ModuleAccessDecision`, `IWorkslipModuleAccess`, and an interim `AllModulesEnabledAccess` default (entitles every module) registered in [DI](../../../src/BE/WorkslipApi/Workslip.Application/DependencyInjection.cs).
- Frontend: the `providers/moduleAccess` layer (`FeatureGate`, `useModuleAccess`, `ModuleAccessProvider`, module keys), defaulting to the `all` sentinel so nothing is hidden.

Deliberately **not** in this change (follow-ups): the product-owned adapter and local entitlement projection; the platform-side module registration under `products/workslip`; per-endpoint `RequireModule` enforcement; delivering the effective-capability summary in the session; and folding the help wizard's stubbed tenant tier onto this path.

## Consequences

- Packaging, navigation and enforcement now share one vocabulary and one decision point, so a module becomes a single declarative entry rather than scattered checks.
- The platform dependency is isolated to one adapter behind a versioned contract, preserving the no-shared-database and product-owns-adapter boundaries.
- Until the adapter lands, every module is enabled, so existing tenants keep all current capabilities — consistent with the migration rule in the blueprint.
- Two gates (`Can` + `FeatureGate`) must be kept in sync at call sites; the route/nav manifest is the intended place to combine them.

## References

- [Workslip modular product blueprint](../workslip-modular-product-blueprint.md)
- [ADR 0014 — MR SAAS'y delivery-lifecycle orchestration boundary](0014-mr-saasy-delivery-lifecycle-orchestration-boundary.md)
- [ADR 0010 — MR SAAS'y control-plane bootstrap boundary](0010-mr-saasy-control-plane-bootstrap-boundary.md)
- [ADR 0005 — main as production boundary](0005-main-as-production-boundary.md)
