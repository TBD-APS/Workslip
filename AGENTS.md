# Workslip agent instruction router

This file routes implementation agents to the scoped instructions for the part of Workslip being changed. Do not duplicate detailed rules here.

## Always read

1. [`Docs/agents/OPERATING_CONTRACT.md`](Docs/agents/OPERATING_CONTRACT.md) — repository workflow, source-of-truth order, scope discipline, security, maintainability, scalability, documentation, and completion language.
2. [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md) — mandatory validation ladder, test selection, Playwright requirements, and evidence standards.
3. [`Docs/compliance/GDPR_AI_ACT_BASELINE.md`](Docs/compliance/GDPR_AI_ACT_BASELINE.md) — mandatory GDPR and EU AI Act change gates, data/AI governance, release blockers, and compliance evidence requirements.

## Global architecture principle

Optimize for low cognitive load, explicit dependencies, predictable code placement, isolated use cases, and low hidden coupling across all layers.

Use patterns only when they reduce complexity, maintenance cost, or change risk more than they introduce. Prefer thin entry points, feature-local logic, consistent contracts, and existing conventions. Avoid wrappers, interfaces, mapping layers, pipelines, and abstractions without a concrete need.

Small improvements that naturally reduce technical debt are encouraged when they stay within the current task's scope. Do not start opportunistic rewrites or widen an issue to pursue an architectural pattern.

## Read for the affected scope

| Scope | Required instructions |
|---|---|
| Frontend under `src/FE/` | [`src/FE/AGENTS.md`](src/FE/AGENTS.md) |
| API/backend under `src/BE/WorkslipApi/` | [`src/BE/WorkslipApi/AGENTS.md`](src/BE/WorkslipApi/AGENTS.md) |
| Infrastructure under `src/BE/infrastructure/` | [`src/BE/infrastructure/AGENTS.md`](src/BE/infrastructure/AGENTS.md) |
| Maintained documentation under `Docs/` | [`Docs/AGENTS.md`](Docs/AGENTS.md) |

For cross-layer changes, read every applicable scoped file before editing. The closest scoped `AGENTS.md` applies in addition to the three shared documents.

Do not begin implementation until the relevant instructions, Linear issue, current branch state, applicable architecture documentation, and compliance impact have been inspected.
