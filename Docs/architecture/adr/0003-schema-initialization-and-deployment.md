# ADR 0003 — Schema initialization and deployment

**Status:** Accepted as current implementation; requires hardening review  
**Date:** 2026-07-23  
**Owner:** Backend/operations  
**Linear:** WOR-142

## Context

The API currently initializes persistence during startup. It calls `EnsureCreatedAsync`, applies EF Core migrations and then executes targeted compatibility SQL for idempotency and notification columns. Production API deployment is performed through GitHub Actions to Azure Web App.

## Decision

- EF Core models and migrations are the primary schema source of truth.
- Application startup currently performs schema initialization before serving requests.
- Compatibility SQL is allowed only when documented, idempotent and temporary.
- Deployment documentation must state the actual workflow, configuration sources and migration behaviour.
- A failed schema initialization must prevent the application from starting.

## Consequences

- Deployment can bring an expected database forward without a separate manual command.
- Startup has database-change privileges and can increase release risk.
- Mixing `EnsureCreated`, migrations and compatibility SQL makes schema ownership harder to reason about.
- Rollback is not guaranteed when a migration is destructive; roll-forward planning is required.

## Alternatives considered

- Dedicated migration job before deployment: safer separation, but not currently implemented.
- Manual production migrations: rejected as the default because it depends on undocumented operator knowledge.
- `EnsureCreated` only: rejected because it does not provide a controlled migration history.

## Follow-up

WOR-143 must define stop/go criteria, backup expectations and rollback/roll-forward handling. The startup initializer should later be simplified so every persistent change has one clear owner.
