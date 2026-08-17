# Shared presentation owner

Root [`../../../../../AGENTS.md`](../../../../../AGENTS.md) and frontend [`../../../AGENTS.md`](../../../AGENTS.md) apply.

This directory is the canonical frontend owner for stable, cross-feature, locale-sensitive presentation rules.

## Before editing presentation behavior

1. Resolve the ownership intent through [`Docs/architecture/owners.json`](../../../../../Docs/architecture/owners.json) or `node tools/agents/resolve-architecture-owner.mjs <intent>` when starting from the repository root.
2. Prefer an existing primitive here before adding a new formatter.
3. Add a new shared primitive only when there is a real product-wide contract or repeated consumer; do not build speculative helper layers.
4. Keep domain wording and business rules in the owning feature. Shared presentation primitives format values; they do not decide business meaning.

## Current contracts

- `locale.ts` owns the product UI locale.
- `date.ts` owns user-visible date and timestamp presentation.
- Canonical date-only output uses Danish abbreviated month text, for example `17. aug. 2026`.
- API/ISO serialization and HTML input values are transport/input contracts, not UI presentation, and must keep their required machine formats.

As number, currency, percent, byte, duration, casing or collation conventions become genuinely shared, they belong here rather than in feature-local utility files.

## Validation

- Add example-based tests for global presentation contracts.
- `npm run check:presentation-formatting` must remain green.
- Do not weaken repository guards or add feature-local bypasses to preserve legacy formatting.
