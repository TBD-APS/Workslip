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
| What did we previously plan or try? | Historical plans/specifications |
| What does law/contract require? | Current authoritative legal sources, signed terms and approved compliance records |

A document may explain an authority; it must not silently replace it.

## Document states

- **Active** — intended to describe current behaviour or procedure.
- **Draft** — incomplete or awaiting a decision; not evidence of implemented behaviour.
- **Historical** — retained for context only.
- **Generated** — derived from another source and not hand-edited.

When an active page becomes redundant, prefer a short superseded pointer over keeping two copies of the same rules alive.

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
| Architecture/ADRs | [`architecture/README.md`](architecture/README.md) | Draft index; accepted ADRs are authoritative decisions |
| API/integrations | [`api/README.md`](api/README.md) | Active |
| Compliance baseline | [`compliance/GDPR_AI_ACT_BASELINE.md`](compliance/GDPR_AI_ACT_BASELINE.md) | Active |
| CI/release expectations | [`operations/ci-quality-gates.md`](operations/ci-quality-gates.md) | Active |
| Pages/domain operations | [`operations/github-pages-domain-runbook.md`](operations/github-pages-domain-runbook.md) | Active |
| Application Insights dashboard | [`operations/APPLICATION_INSIGHTS_ERROR_DASHBOARD.md`](operations/APPLICATION_INSIGHTS_ERROR_DASHBOARD.md) | Active |

## Writing rules that prevent drift

1. State **current state** directly. Do not make current wording depend on a future issue changing state; link the issue as history/context instead.
2. Keep durable decisions in ADRs, not scattered comments and READMEs.
3. Keep exact command names close to the package/tool that owns them.
4. Prefer generated/runtime contracts over manually copied catalogs.
5. Avoid line-by-line implementation descriptions unless they are operationally important and expensive to rediscover.
6. Remove duplicate active rules rather than trying to synchronize them.

## Historical material

These locations are useful context but are not current implementation truth:

- `Docs/superpowers/plans/`
- `Docs/superpowers/specs/`
- `.hermes/specs/`
- `src/docs/`
- documents explicitly marked Historical/Superseded

## Drift check

Run:

```bash
python tools/docs/check_docs.py
```

The checker verifies high-value structural facts directly against the repository: local links, referenced npm scripts, retired duplicate artifacts, issue-status-dependent wording and exact duplicated agent rules. It is intentionally small and dependency-free; it does not attempt to prove semantic correctness.

Reviewers still verify claims against their primary authority.
