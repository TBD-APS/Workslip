# Workslip documentation

**Status:** Active  
**Owner:** Workslip maintainers  
**Review cadence:** When repository structure or documentation policy changes

Documentation should make important behaviour easier to understand without becoming a second implementation that must be kept in sync by hand.

## Truth model

| Question | Primary authority |
|---|---|
| What does the software do now? | Current code, checked-in configuration, schema/mappings and executable tests |
| What is the public/runtime API contract? | Endpoint registrations and runtime OpenAPI |
| Why was a durable technical choice made? | Accepted ADR |
| How is production operated? | Current infrastructure/workflow definitions plus maintained runbooks |
| What work is planned/in progress? | Linear |
| What did we previously plan or try? | Git/PR/Linear history |
| What does law/contract require? | Current authoritative legal sources, signed terms and approved compliance records |

A document may explain an authority; it must not silently replace it.

## Document states

- **Active** — intended to describe current behaviour or procedure.
- **Draft** — incomplete or awaiting a durable decision; not evidence of implemented behaviour.
- **Historical** — allowed only when the historical context itself has durable architecture/operations value, normally as an ADR state or short retained record.
- **Generated** — derived from another source and not hand-edited.

Do not keep issue implementation plans, completed task specs or superseded runbooks beside current guidance merely so historical links continue to resolve. Git, PR and Linear history already preserve that material. When an active page becomes redundant and has no durable decision/operations value, delete it.

## Maintained entrypoints

| Area | Document | State |
|---|---|---|
| Repository | [`../README.md`](../README.md) | Active |
| Agent rules | [`../AGENTS.md`](../AGENTS.md) | Active |
| Documentation rules | [`AGENTS.md`](AGENTS.md) | Active |
| Validation | [`agents/VALIDATION.md`](agents/VALIDATION.md) | Active |
| Frontend | [`../src/FE/README.md`](../src/FE/README.md) | Active |
| Backend/API host | [`../src/BE/WorkslipApi/README.md`](../src/BE/WorkslipApi/README.md) | Active |
| Infrastructure | [`../src/BE/infrastructure/README.md`](../src/BE/infrastructure/README.md) | Active |
| Database migrations | [`../src/BE/infrastructure/database/migrations/README.md`](../src/BE/infrastructure/database/migrations/README.md) | Active |
| Architecture/ADRs | [`architecture/README.md`](architecture/README.md) | Active index; accepted ADRs are authoritative decisions |
| API/integrations | [`api/README.md`](api/README.md) | Active |
| Email operations | [`acs-email-setup.md`](acs-email-setup.md) | Active |
| Compliance baseline | [`compliance/GDPR_AI_ACT_BASELINE.md`](compliance/GDPR_AI_ACT_BASELINE.md) | Active |
| CI/release expectations | [`operations/ci-quality-gates.md`](operations/ci-quality-gates.md) | Active |
| Pages/domain operations | [`operations/github-pages-domain-runbook.md`](operations/github-pages-domain-runbook.md) | Active |
| Application Insights dashboard | [`operations/APPLICATION_INSIGHTS_ERROR_DASHBOARD.md`](operations/APPLICATION_INSIGHTS_ERROR_DASHBOARD.md) | Active |
| Public site content | [`../site/README.md`](../site/README.md) | Active content surface |

## Writing rules that prevent drift

1. State **current state** directly. Do not make current wording depend on a future issue changing state; link the issue as history/context instead.
2. Keep durable decisions in ADRs, not scattered comments and READMEs.
3. Keep exact command names close to the package/tool that owns them.
4. Prefer generated/runtime contracts over manually copied catalogs.
5. Avoid line-by-line implementation descriptions unless they are operationally important and expensive to rediscover.
6. Remove duplicate active rules rather than trying to synchronize them.
7. Keep completed issue plans/specs in Git/PR/Linear history instead of maintaining a parallel historical documentation tree.

## Drift check

Run:

```bash
python tools/docs/check_docs.py
```

The checker validates all Markdown under `Docs/` as an explicitly maintained surface, plus public site Markdown and key package/infrastructure READMEs. It checks local links, referenced npm scripts, retired artifacts/documentation paths, issue-status-dependent wording, API/architecture index coverage and exact duplicated agent rules. Historical issue-plan directories are rejected so they cannot silently return as a second source of truth.

The checker is intentionally dependency-free and still cannot prove semantic correctness. Reviewers must verify technical claims against current code/config/tests and operational claims against the current workflow/infrastructure definition.
