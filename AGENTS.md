# Workslip agent instruction router

This file routes implementation agents to the scoped instructions for the part of Workslip being changed. Do not duplicate detailed rules here.

## Always read

1. [`Docs/agents/OPERATING_CONTRACT.md`](Docs/agents/OPERATING_CONTRACT.md) — repository workflow, source-of-truth order, scope discipline, security, maintainability, scalability, documentation, and completion language.
2. [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md) — mandatory validation ladder, test selection, Playwright requirements, and evidence standards.

## Read for the affected scope

| Scope | Required instructions |
|---|---|
| Frontend under `src/FE/` | [`src/FE/AGENTS.md`](src/FE/AGENTS.md) |
| API/backend under `src/BE/WorkslipApi/` | [`src/BE/WorkslipApi/AGENTS.md`](src/BE/WorkslipApi/AGENTS.md) |
| Infrastructure under `src/BE/infrastructure/` | [`src/BE/infrastructure/AGENTS.md`](src/BE/infrastructure/AGENTS.md) |
| Maintained documentation under `Docs/` | [`Docs/AGENTS.md`](Docs/AGENTS.md) |

For cross-layer changes, read every applicable scoped file before editing. The closest scoped `AGENTS.md` applies in addition to the two shared documents.

Do not begin implementation until the relevant instructions, Linear issue, current branch state, and applicable architecture documentation have been inspected.
