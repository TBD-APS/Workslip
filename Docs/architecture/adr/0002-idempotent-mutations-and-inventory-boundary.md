# ADR 0002 — Idempotent mutations and future inventory boundary

**Status:** Accepted for current API mutations; proposed for inventory posting  
**Date:** 2026-07-23  
**Owner:** Backend architecture  
**Linear:** WOR-142

## Context

Workslip performs state-changing API calls from a browser/PWA where retries, double clicks and uncertain network outcomes are possible. Current job create, update and status endpoints require an `Idempotency-Key`, reserve a request scope and replay a completed response when the same request is repeated.

Inventory and material consumption are not implemented in the current codebase. Any future stock posting must not be described as existing behaviour.

## Decision

1. Mutating clients send an `Idempotency-Key`.
2. The API scopes a key to operation, organization, user and resource where relevant.
3. The server stores request hash, reservation state and completed response.
4. Reuse with a different payload is rejected; successful replay returns the stored result.
5. In-flight client deduplication is only a UX safeguard. Server-side idempotency remains authoritative.
6. Future inventory posting and job submission must execute in one server-side database transaction and use the same idempotent command boundary.

## Consequences

- Repeated supported mutations can be processed safely.
- Clients must not silently invent retry behaviour for non-idempotent operations.
- The idempotency store requires expiry and cleanup.
- Inventory work remains blocked until its transaction, concurrency and reversal rules are implemented and tested.

## Alternatives considered

- Client-only double-click protection: rejected because it cannot handle retries or lost responses.
- Separate “post stock” and “submit job” calls: rejected for future inventory because partial failure would corrupt business state.
- Treat every mutation as naturally idempotent: rejected because request semantics differ.

## Evidence

Current implementation lives in job endpoints, `IdempotencyStore`, idempotency persistence and the frontend Axios interceptor. No inventory domain model or stock transaction implementation is currently verified.
