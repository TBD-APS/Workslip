# Workslip agent rules

This file contains the repository-wide rules that implementation agents must follow. Scoped `AGENTS.md` files add only rules that are specific to their directory.

## Before changing code

1. Inspect the current branch/worktree and read the Linear issue that owns the change.
2. Read the closest applicable scoped `AGENTS.md` file.
3. Inspect the current implementation, tests, configuration, schema and active ADRs before making assumptions.
4. Read [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md) when implementing or validating a change.
5. Read [`Docs/compliance/GDPR_AI_ACT_BASELINE.md`](Docs/compliance/GDPR_AI_ACT_BASELINE.md) only when personal-data processing, an external processor, or an AI system is affected.

Do not begin editing until the branch belongs to one cohesive issue and the affected implementation is understood.

## Source of truth

For implemented technical behaviour, use this order:

1. current source code, checked-in configuration, database mappings/migrations and executable tests;
2. runtime-generated contracts and verified infrastructure definitions;
3. accepted ADRs and maintained operational/compliance documentation;
4. Linear for scope, priority, ownership and delivery status;
5. dated plans/specifications for historical context only.

Generated repository snapshots are not a source of truth. Inspect the current repository directly.

When documentation disagrees with implementation, fix the maintained documentation in the same change unless the implementation itself is the bug.

## Branch and scope discipline

- Never push directly to `main`.
- One Linear issue per branch and pull request.
- Branch: `rbj--<issue>-<description>`.
- PR title: `RBJ-<issue>: <description>`.
- Prefer small, cohesive PRs and squash merging.
- Do not mix unrelated cleanup into feature work.
- Improve nearby technical debt only when it is required for correctness, materially lowers risk, or removes duplication inside the task boundary.

## Engineering defaults

- Keep frontend, backend, infrastructure and external integrations behind clear boundaries.
- Prefer existing shared components, services, repositories, validators and conventions.
- Do not add wrappers, abstractions, dependencies or patterns without a concrete current need.
- Keep entry points thin and business rules in the appropriate application/domain layer.
- Treat frontend authorization as UX only; authorization and tenant isolation are backend responsibilities.
- Review transactions, retries, idempotency, concurrency, partial failure, cache isolation and sensitive logging where relevant.
- Do not weaken tests or guards to make a change pass.

If a verified bug, security issue, data-integrity risk or architectural violation is discovered inside the affected area, fix it when it belongs to the same cohesive change; otherwise report it and create/link follow-up work.

## Safety and data

Do not commit credentials, tokens, private keys, production personal data, restricted contracts or incident/rights-request material.

Stop and escalate before destructive production operations, irreversible data semantics, unapproved processor/data transfers, or AI capabilities that require a legal/product decision. Engineering may recommend the decision but must not invent legal approval.

## Validation and completion

Run the smallest validation set that proves the changed risk. Follow [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md) for the required level.

Report evidence precisely: static review, build, automated tests, integration tests, Playwright, deployed smoke and compliance/operational evidence are different things. Do not say “done”, “works” or “validated” without stating what actually ran and what remains unverified.

## Documentation and decisions

- Prefer changing one maintained document over creating a competing source.
- State current facts as facts, decisions as decisions, and planned work as planned work.
- Do not make maintained documentation depend on an issue eventually being completed; describe the current state and link the issue only for context.
- Record significant architecture/security/privacy decisions as ADRs.
- Record important chat decisions in the repository or Linear.
- Do not hand-edit generated contracts or clients; change their source and regenerate them.

## Scoped instructions

| Area | Additional rules |
|---|---|
| Frontend `src/FE/` | [`src/FE/AGENTS.md`](src/FE/AGENTS.md) |
| Backend/API `src/BE/WorkslipApi/` | [`src/BE/WorkslipApi/AGENTS.md`](src/BE/WorkslipApi/AGENTS.md) |
| Infrastructure `src/BE/infrastructure/` | [`src/BE/infrastructure/AGENTS.md`](src/BE/infrastructure/AGENTS.md) |
| Maintained docs `Docs/` | [`Docs/AGENTS.md`](Docs/AGENTS.md) |

For cross-layer changes, apply every relevant scoped file.
