# Automation ownership boundaries

Issue: WOR-693

## Goal

Refactor repository automation around clear ownership domains rather than a target number of GitHub Action files.

The invariant is simple:

> Delivery must not know about Kimi, Cerebras or model routing, and AI runtime must not own Azure deployment, migrations or release semantics.

GitHub Actions should be thin execution entrypoints. Runtime logic and policy belong in the module that owns the concern.

## Ownership domains

### Delivery / Release

Owns deterministic delivery only:

- canonical CI/build/test
- exact-SHA release eligibility
- backend production deploy
- database migrations
- production smoke/readiness
- infrastructure reconcile
- release artifacts and environment policy

Current first-class workflows in this domain include:

- `frontend-validation.yml`
- `delivery-ci-checkpoint.yml`
- `backend-production-deploy.yml`
- `database-production-migrations.yml`
- `infrastructure-production-reconcile.yml`
- `production-readiness-smoke.yml`
- `linear-release.yml`

Delivery implementation helpers should converge behind `tools/delivery/**` or equivalent delivery-owned modules. Workflow YAML remains responsible for triggers, permissions, environments and invoking a stable entrypoint.

`delivery-ci-checkpoint.yml` is the trusted delivery observer for the canonical `CI` workflow and backend deployment workflow. It checks out only the default-branch sender and forwards sanitized lifecycle checkpoints through the delivery helper; it never executes pull-request source with Control Center credentials.

### AI / Agent Runtime

Owns agent intelligence and execution:

- task classification
- provider/model discovery and routing
- Moonshot/Kimi, Cerebras, OpenAI and Anthropic adapters
- prompt/context construction
- implementation workers
- repair loops
- exact-head multi-agent reviews
- challenger/shadow execution
- benchmark and telemetry hooks
- promotion policy

Target ownership is Sassy runtime code such as:

```text
platform/mr-saasy-control-plane/agent-runtime/
  orchestration/
  routing/
  providers/
  workers/
  reviews/
  sandbox/
  telemetry/
  benchmarks/
```

GitHub should expose one/few generic thin entrypoints for this runtime. A Linear feature must not create a feature-specific workflow.

`ai-pr-review.yml` can retain its trigger/check contract while provider selection, prompts, execution and aggregation are migrated into the Sassy-owned runtime.

### Repository Governance

Owns repository truth and static invariants:

- documentation truth
- architecture boundaries
- SQL ownership guards
- repository data hygiene
- static trust-boundary checks

Current examples:

- `feature-change-guard.yml`
- `owned-sql-guard.yml`
- `repository-data-hygiene.yml`

These do not need to be collapsed merely to reduce workflow count. Independent checks are acceptable when the boundary and check name are valuable. Consolidation is secondary to clear ownership.

### Experiments / POCs

Owns temporary proofs and destructive experiments.

Current temporary examples:

- `mr-saasy-agent-poc.yml` — retained while it proves durable-loop and disposable-sandbox boundaries.
- `ai-pr-review-selftest.yml` and `production-delivery-selftest.yml` — candidates for retirement or migration into owning module tests after consumer audit.

Every experiment needs an explicit promotion/retirement condition and must not silently become permanent production automation.

## WOR-693 Phase 0 removals

The following are removed because they are obsolete or task-specific runtime debt:

- `kimi-agent-heartbeat.yml` — provider bootstrap placeholder.
- `kimi-first-feature-exam.yml` — one-off WOR-620 Kimi certification harness.
- `wor674-kimi-webapp-redesign.yml` — feature-specific agent orchestration.
- `.github/wor674/kimi_worker.py` — feature-specific agent runtime.

## Target monorepo shape

```text
.github/
  workflows/
    delivery-*.yml
    ai-*.yml
    governance-*.yml

platform/
  mr-saasy-control-plane/
    agent-runtime/
      orchestration/
      routing/
      providers/
      workers/
      reviews/
      sandbox/
      telemetry/
      benchmarks/

tools/
  delivery/
    release/
    migrations/
    validation/
```

The naming/count is illustrative. Ownership is the architecture contract.

## Runtime flow

```text
Linear / GitHub task
        |
        v
thin AI workflow entrypoint
        |
        v
MR SAAS'y Agent Runtime
  task classifier
        |
  model router
        |
  provider adapter
        |
  sandboxed worker
        |
  deterministic gates
        |
  repair / exact-head reviews
        |
  evidence + telemetry
        |
        v
GitHub PR
```

Delivery remains a separate deterministic path:

```text
validated exact SHA
        |
        v
Delivery workflow
        |
  release eligibility
  build artifact
  migrate/reconcile where explicitly allowed
  deploy
  smoke
        |
        v
production evidence
```

Neither path owns the other's internal policy.

## Long-term extraction seam

Do not split repositories yet. First make the monorepo module/API boundaries stable enough that extraction becomes mechanical.

Future candidates:

- `mrsoftware/workslip` — product/domain code.
- `mrsoftware/sassy-agent-runtime` — provider-neutral agent platform.
- `mrsoftware/infrastructure` — shared delivery/cloud infrastructure where a real shared boundary exists.

Workslip should eventually request operations such as `review this exact PR head` or `implement this bounded task` without embedding model/provider implementation in the product repository.

## Invariants

- A new Linear feature must not create `worXXX-*.yml` or `.github/worXXX/` runtime code.
- Delivery workflows contain no model/provider/prompt logic.
- AI workflows contain no Azure production deployment/migration policy.
- Repository governance remains deterministic and provider-independent.
- POCs have explicit retirement/promotion conditions.
- Required check names and release consumers are audited before workflow rename/removal.
- Production delivery semantics are unchanged by WOR-693 Phase 0.
