# Claude repository entrypoint

This file is intentionally small. Claude-specific sessions must not maintain a second copy of Workslip's repository handbook.

## Read first

1. [`AGENTS.md`](AGENTS.md) — authoritative repository-wide engineering and delivery rules.
2. [`Docs/agents/AGENT_HANDBOOK.md`](Docs/agents/AGENT_HANDBOOK.md) — provider-neutral agent operating model, role/provider separation, checkpoints and privacy defaults.
3. The closest scoped `AGENTS.md` for every path being changed.
4. The owning Linear issue plus relevant accepted ADRs/current maintained docs.

When guidance conflicts, follow the source-of-truth order in `AGENTS.md`. Current code/config/schema/tests outrank orientation prose.

## Claude role

Claude is normally used as an independent code/security/architecture reviewer. When delegated implementation work, it follows the same branch, scope, validation and evidence rules as every other implementation agent.

Do not approve a change merely because Claude authored it. Critical review should remain independent from implementation when the delivery flow requires separation of duties.

## Working context

Do not hard-code session branches, issue IDs, current PRs, model names or temporary delivery state in this file. Resolve them at session startup from GitHub/Linear and publish checkpoints through the shared Control Center contract when supported.

For repository orientation, start from [`README.md`](README.md) and [`Docs/README.md`](Docs/README.md). For validation use [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md); for agent handoffs use [`Docs/agents/DELIVERY_HANDOFFS.md`](Docs/agents/DELIVERY_HANDOFFS.md).
