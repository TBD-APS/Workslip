# ADR 0004 — Offline and draft principle

**Status:** Accepted principle; full durable draft flow not verified  
**Date:** 2026-07-23  
**Owner:** Frontend/product  
**Linear:** WOR-142

## Context

Workslip is installed as a PWA and precaches application assets. The current service worker activates updates immediately and can reload open clients. The frontend depends on the API for business operations. A durable tenant/user-scoped offline draft implementation is not verified in the current source.

## Decision

- Cached application assets do not mean that business operations work offline.
- Submit, approval and other server mutations require a confirmed online response.
- The UI must not describe data as synchronized until the server confirms it.
- Any local draft must be scoped by organization, user and job, preserve failed input and expose its local/synced state.
- Automatic application updates must not discard unsaved form state.
- Full offline mutation queues are out of scope until conflict, ordering and authorization semantics are designed.

## Consequences

- User messaging remains conservative and truthful.
- A durable draft feature needs explicit storage, cleanup and cross-account isolation tests.
- PWA update behaviour requires hardening before long dirty forms can be considered safe.
- Offline support is a product capability with data-integrity requirements, not only a service-worker setting.

## Alternatives considered

- Promise offline support based on precaching alone: rejected.
- Queue every failed mutation automatically: rejected because ordering, conflicts and expired authorization are unresolved.
- Store drafts without tenant/user namespacing: rejected because it risks cross-account data exposure.

## Evidence

Vite config enables an inject-manifest PWA. The current registration and service worker activate updates automatically. No verified durable IndexedDB draft workflow was found beyond the presence of an IndexedDB utility dependency.
