# Workslip documentation index

This directory separates maintained product documentation from historical implementation notes.

## Document states

- **Active:** expected to match the current repository and reviewed on the stated cadence.
- **Draft:** incomplete or awaiting a decision; it must not be treated as implemented behaviour.
- **Historical:** retained for context only. Current code, Linear and active documents take precedence.
- **Generated:** produced from code or tools and must not be edited manually unless its generator says otherwise.

## Maintained documents

| Area | Document | State | Owner | Review cadence | Primary source of truth |
|---|---|---:|---|---|---|
| Repository entrypoint | [`../README.md`](../README.md) | Active | Workslip maintainers | Monthly and on setup changes | Repository structure and current commands |
| Frontend development | [`../src/FE/README.md`](../src/FE/README.md) | Active | Frontend owner | On frontend tooling/config changes | `package.json`, Vite config and frontend source |
| Backend development | [`../src/BE/WorkslipApi/README.md`](../src/BE/WorkslipApi/README.md) | Active | Backend owner | On API/persistence/config changes | Backend source, configuration and workflows |
| Infrastructure deployment | [`../src/BE/infrastructure/README.md`](../src/BE/infrastructure/README.md) | Active | Repository owner | On Azure, Entra, SQL or secret changes | Bicep, deployment scripts and accepted ADRs |
| Architecture and ADRs | [`architecture/README.md`](architecture/README.md) | Draft | Architecture owner | Monthly and on boundary changes | Deployed code, infrastructure and accepted ADRs |
| API and integrations | [`api/README.md`](api/README.md) | Draft | API owner | On contract changes | Runtime OpenAPI, endpoint code and Postman suite |
| Pages and domain operations | [`operations/github-pages-domain-runbook.md`](operations/github-pages-domain-runbook.md) | Active | Repository owner | Before Pages or DNS changes | GitHub Pages settings, DNS and Pages workflow |
| CI quality gates | [`operations/ci-quality-gates.md`](operations/ci-quality-gates.md) | Active | Repository owner | Monthly and on required-check changes | Workflows, rulesets and successful runs |
| Application Insights error dashboard | [`operations/APPLICATION_INSIGHTS_ERROR_DASHBOARD.md`](operations/APPLICATION_INSIGHTS_ERROR_DASHBOARD.md) | Active | Workslip maintainers | On telemetry, Azure RBAC, KQL or incident-process changes | Diagnostics endpoint/service, frontend telemetry and Azure configuration |
| Agent conventions | [`../AGENTS.md`](../AGENTS.md) | Active | Repository owner | On engineering-policy changes | Approved repository rules |

Named owners should be replaced with actual team members when ownership is assigned in Linear. Until then, the repository owner is accountable for review.

## Historical and planning material

The following locations contain useful context but are not automatically current:

- `Docs/superpowers/plans/` — dated implementation plans.
- `Docs/superpowers/specs/` — dated feature specifications.
- `.hermes/specs/` — historical agent-generated specifications.
- `src/docs/` — product and implementation plans, including proposed future behaviour.
- `Docs/testing/full-stack-validation.md` — historical description of the workflow removed under WOR-188.

A historical document should retain its original context or carry a visible historical status. Do not treat it as active automation.

## Source-of-truth precedence

When documents disagree, use this order:

1. Current source code, checked-in configuration, database mappings/migrations and executable tests.
2. Runtime-generated contracts such as OpenAPI, plus verified deployment/infrastructure definitions.
3. Accepted ADRs and maintained runbooks.
4. Linear for scope, priority, ownership and delivery status.
5. Dated plans/specifications for historical context only.
6. Repomix snapshots for navigation only; verify against the current file before changing code or documentation.

## Required documentation impact review

Every pull request must consider documentation when it changes any of these areas:

- API request/response/error contracts
- authentication, authorization or tenant boundaries
- database ownership, lifecycle or migrations
- deployment, configuration or runtime dependencies
- user journeys, offline behaviour or limitations
- operational alerts, backup/restore or incident handling

The automated Documentation Quality workflow validates maintained documents and PR documentation decisions. Reviewers must still confirm that the selected documentation decision accurately matches the change.
