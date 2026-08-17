# GitHub Actions convergence

Issue: WOR-693

## Goal

Keep GitHub Actions as a thin execution and CI surface. Agent/provider logic, prompts, repair loops and task-specific orchestration belong in MR SAAS'y runtime code, not in feature-specific workflow YAML.

## Current inventory on main

### Keep as first-class workflows for now

- `frontend-validation.yml` — canonical CI gate. Do not rename until branch protection/check consumers are audited.
- `backend-production-deploy.yml` — exact-green-main-SHA backend delivery.
- `database-production-migrations.yml` — production migration boundary.
- `infrastructure-production-reconcile.yml` — production infrastructure reconciliation.
- `production-readiness-smoke.yml` — production smoke/readiness checks.
- `linear-release.yml` — release integration.
- `pages.yml` — GitHub Pages publishing.
- `mobile-local-session.yml` — retained until its replacement/runtime owner is decided.
- `playwright-local-assignment.yml` — retained while assignment/browser coverage is still independently useful.

### Keep temporarily while Sassy runtime is under development

- `mr-saasy-control-plane-gate.yml` — thin CI gate for the Sassy control plane.
- `mr-saasy-agent-poc.yml` — temporary POC validation for durable loop and disposable sandbox boundaries. Remove after equivalent tests live under the production Sassy runtime gate.

### Migrate out of workflow YAML

- `ai-pr-review.yml` — keep trigger semantics initially, but move provider selection, prompt execution and aggregation into Sassy runtime.

### Consolidate later

- `feature-change-guard.yml`
- `owned-sql-guard.yml`
- `repository-data-hygiene.yml`
- `ai-pr-review-selftest.yml`
- `production-delivery-selftest.yml`

These should become CI jobs, reusable checks, or runtime tests only after required check names and release consumers are audited.

### Remove in WOR-693 Phase 0

- `kimi-agent-heartbeat.yml` — manual secret/bootstrap placeholder; superseded by provider-neutral runtime work.
- `kimi-first-feature-exam.yml` — one-off Kimi certification harness for WOR-620; proof already exists and must not remain canonical runtime.
- `wor674-kimi-webapp-redesign.yml` — task-specific feature orchestration; violates the rule that new Linear features must not require new workflows.
- `.github/wor674/kimi_worker.py` — task-specific agent runtime coupled to the removed WOR-674 workflow.

## Target architecture

A new agentic feature should use one stable Sassy execution entrypoint:

```text
GitHub / Linear event
        |
        v
thin agent-run workflow
        |
        v
MR SAAS'y Agent Runtime
  - task classifier
  - model router
  - provider adapters
  - sandbox/worker
  - deterministic gates
  - repair loop
  - exact-head review coordinator
  - telemetry/promotion policy
        |
        v
GitHub PR / evidence
```

## Invariant

**A new Linear feature must not create a new `worXXX-*.yml` workflow or `.github/worXXX/` runtime directory.**

Production delivery, migration and infrastructure workflows are intentionally unchanged by Phase 0.