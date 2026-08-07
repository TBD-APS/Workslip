# ADR 0004: Retire maintained repository snapshots

**Status:** Accepted  
**Date:** 2026-08-08  
**Decision owners:** Workslip maintainers  
**Linear:** WOR-360

## Context

Workslip maintained `repomix-output.xml` plus privileged automation that periodically regenerated and pushed the packed repository back to `main`.

That artifact duplicated the repository, could be stale between refreshes, consumed review/search context and required a GitHub App with write/bypass capability. During the documentation review the checked-in snapshot was empty while maintained documentation still described snapshot lifecycle details. The snapshot therefore added another state to synchronize without being authoritative.

A similar problem existed in the hand-maintained API endpoint catalog: route registrations and runtime OpenAPI already define the route set, while the copied catalog could lag behind them.

## Decision

Do not maintain a packed full-repository snapshot in source control or release automation.

Agents and developers inspect the current repository directly using normal repository search, file reads, `rg`/IDE tooling and generated/runtime contracts.

Use generated sources only where they are the natural contract (for example runtime OpenAPI). Do not create manually synchronized catalogs of facts that can be derived reliably from current code.

The documentation checker should validate selected high-value facts against their actual sources rather than compare one documentation copy with another repository copy.

## Consequences

- `repomix-output.xml` and its regeneration workflow are removed.
- The dedicated snapshot-publisher credentials/ruleset bypass are no longer required and should be removed from GitHub settings if still configured.
- Documentation and agent rules no longer treat Repomix as a lookup step or source of truth.
- The manual API endpoint catalog becomes a historical pointer to endpoint source/OpenAPI.
- Historical references may remain for traceability but are not current guidance.

This intentionally trades one convenient packed file for lower drift, lower privilege and simpler source-of-truth rules.
