# Workslip agent rules

This file contains the repository-wide rules that implementation agents must follow. Scoped `AGENTS.md` files add only rules that are specific to their directory.

All agent roles/providers must also load the provider-neutral [`Shared Agent Handbook`](Docs/agents/AGENT_HANDBOOK.md). It defines the common MR SAAS'y/Workslip mental model, role/provider separation, Control Center checkpoint contract, privacy defaults and plug-and-play onboarding expectations. This `AGENTS.md` remains authoritative for repository engineering rules when wording overlaps.

## Before changing code

1. Inspect the current branch/worktree and read the Linear issue that owns the change.
2. Resolve relevant shared ownership through [`Docs/architecture/owners.json`](Docs/architecture/owners.json) before inventing a new cross-feature helper, convention or platform boundary. Agents/runtimes may use `node tools/agents/resolve-architecture-owner.mjs <intent>` for machine-readable lookup.
3. Read the closest applicable scoped `AGENTS.md` file and every instruction file returned by the architecture owner registry for the work being changed.
4. Inspect the current implementation, tests, configuration, schema and active ADRs before making assumptions.
5. Read [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md) when implementing or validating a change.
6. Read [`Docs/agents/DELIVERY_HANDOFFS.md`](Docs/agents/DELIVERY_HANDOFFS.md) when work is handed between agents/sessions or when planning, reviewing or release-gating a change.
7. Read [`Docs/compliance/GDPR_AI_ACT_BASELINE.md`](Docs/compliance/GDPR_AI_ACT_BASELINE.md) only when personal-data processing, an external processor, or an AI system is affected.
8. Read [`Docs/strategy/WORKSLIP_STRATEGY.md`](Docs/strategy/WORKSLIP_STRATEGY.md) before cross-functional planning, broad feature prioritization, market-facing work or architecture work that requires product/strategy trade-offs.

Do not begin editing until the branch belongs to one cohesive issue and the affected implementation is understood.

## Architecture owner discovery

[`Docs/architecture/owners.json`](Docs/architecture/owners.json) is the machine-readable map for current shared repository boundaries. It complements scoped `AGENTS.md` files; it does not replace source code, ADRs or Linear.

- Before creating a new shared helper, formatter, token, provider integration, cross-feature primitive or platform abstraction, resolve the relevant intent in the owner registry first.
- If an owner exists, inspect its `path` and read its `instructions` before editing. Prefer extending that owner over creating a parallel implementation.
- If no owner exists, do not guess a future architecture. Inspect current code/ADRs and create a new registry entry only when a real stable boundary is introduced in the same change.
- Registry entries must point only to paths and instruction files that currently exist. Planned architecture belongs in ADRs/Linear until implemented.
- Agent runtimes should load the registry during repository bootstrap so provider/model changes do not change architecture knowledge.
- `node tools/agents/validate-architecture-owners.mjs` and Repository Data Hygiene enforce registry integrity and unambiguous intent ownership.

## Source of truth

For implemented technical behaviour, use this order:

1. current source code, checked-in configuration, database mappings/migrations and executable tests;
2. runtime-generated contracts and verified infrastructure definitions;
3. accepted ADRs and maintained operational/compliance documentation;
4. Linear for scope, priority, ownership and delivery status;
5. dated plans/specifications for historical context only.

For current product/company strategy, ICP, go-to-market direction and cross-functional ownership defaults, use [`Docs/strategy/WORKSLIP_STRATEGY.md`](Docs/strategy/WORKSLIP_STRATEGY.md). It does not override code/ADRs for technical truth or Linear for live issue status.

Generated repository snapshots are not a source of truth. Inspect the current repository directly.

When documentation disagrees with implementation, fix the maintained documentation in the same change unless the implementation itself is the bug.

## Branch and scope discipline

- Never push directly to `main`.
- One Linear issue per implementation branch and pull request. Repository-governance-only changes explicitly requested by the repository owner may omit a Linear issue.
- Branch: `rbj--<issue>-<description>`. The double dash is load-bearing: the `Workslip active feature protection` ruleset in [`tools/release/configure-github-branch-rules.ps1`](tools/release/configure-github-branch-rules.ps1) matches `rbj--*`, so a branch named with a single dash gets no deletion or non-fast-forward protection at all.
- PR title: `RBJ-<issue>: <description>`, unless the [pull request template](.github/pull_request_template.md) is being followed, which specifies `[Type] ISSUE-ID — Clear title`. These two conventions currently disagree and neither is enforced by CI; WOR-770 owns settling it. Until it does, match whichever the reviewer expects rather than assuming this line is authoritative.
- Prefer small, cohesive PRs and squash merging.
- **Prefer Git stacks by default for related, ordered or overlapping work.** Keep one cohesive issue per stack layer, create each child branch from the previous stack branch, and target the child PR at its parent branch while the stack is active.
- Do not create multiple parallel PRs against `main` or a release branch when the changes belong to the same delivery sequence, touch shared implementation, or have an intended merge order. Extend the existing stack instead.
- Use a standalone PR directly from `main` or a release branch only when the change is genuinely independent, has no relevant dependency or overlap with an active stack, and can be reviewed, merged and deployed in any order.
- Before opening a new PR, inspect related active branches/PRs and attach the work to the existing stack when one exists.
- When multiple agents or sessions are active, inspect expected file/scope overlap before editing. If overlap exists, coordinate in a durable Linear or PR comment and choose one owner instead of creating competing implementations.
- Keep the stack order explicit in PR descriptions. As parent layers merge, rebase or retarget the next layer instead of recreating equivalent parallel PRs unless a verified GitHub limitation makes replacement unavoidable.
- Do not mix unrelated cleanup into feature work.
- Improve nearby technical debt only when it is required for correctness, materially lowers risk, or removes duplication inside the task boundary.

## Customer-value gate

For new customer-facing features, product improvements and material scope expansion, apply [`Docs/agents/CUSTOMER_VALUE_GATE.md`](Docs/agents/CUSTOMER_VALUE_GATE.md) before implementation.

- Separate the customer problem from the requested solution.
- Prefer the smallest complete slice that removes measurable pain using existing Workslip patterns.
- Treat observed workarounds and repeated manual effort as evidence of latent demand.
- Evaluate money/cashflow, paid time, resource leverage, risk, frequency, reach, adoption friction, effort, maintenance and evidence confidence.
- End product triage with one recommendation: **DO NOW**, **VALIDATE FIRST**, **DEFER** or **REJECT / REFRAME**.
- Do not let a commercial score override security, tenant isolation, authorization, data integrity, compliance or release safety.

Urgent security, tenant-isolation, data-loss, compliance and production-correctness fixes do not wait for commercial scoring; use the gate only to keep their correction small and coherent.

## Lifecycle engineering

Workslip is operated as a long-lived, integrated service, not as a queue of isolated tickets. For a material change, make the intended customer or operational outcome, constraints and measurable acceptance conditions explicit before choosing the implementation. Keep the process lightweight for small, well-understood corrections.

The normal delivery path remains **idea → code → independent review → go live**. Lifecycle engineering makes the transitions evidence-based inside that flow; it does not impose serial workshops, a separate phase-gate process or a delay before a low-risk change can be built and shipped.

Apply these principles when they match the changed risk:

1. **Feasibility and concept:** explore the smallest credible options before committing when the change affects product direction, architecture, data semantics, security, cost or operability. Record the selected option and the constraints it satisfies; do not treat an untested assumption as a requirement.
2. **Definition and interfaces:** define the responsibility, inputs, outputs, ownership, failure behaviour and compatibility expectations of every material boundary. Treat API, event, schema, infrastructure and user-journey contracts as interfaces that must remain coherent across teams and releases.
3. **Concurrent engineering:** involve the relevant product, engineering, QA, security, data, SRE/operations and support perspectives early enough to change the design cheaply. Keep role independence and the smallest necessary group; this is coordinated design, not blanket delegation or a new approval layer.
4. **Digital thread and traceability:** maintain a verifiable chain from need → decision → implementation → executable contract/test → deployment and operational evidence. Use maintained code, configuration, tests, ADRs, Linear and PR evidence as the linked record; do not rely on chat summaries or duplicate status documents.
5. **Integration and qualification:** validate in ascending fidelity: focused deterministic checks first, then interface/feature evidence, then safe environment and deployed smoke where the risk requires it. Exercise the important failure, recovery, compatibility and rollback paths—not only the happy path—and state any evidence that is still missing.
6. **Operability by design:** design changes so they can be observed, supported, maintained, upgraded and safely reversed. Consider access, diagnostics, alerts, capacity, deployment/rollback, data migration and support workflows before crossing a production boundary.

For material cross-boundary changes, establish a lightweight **readiness checkpoint** before irreversible rollout: the selected scope, interfaces, acceptance evidence, rollout/rollback plan and accountable owner must be explicit. It prevents accidental scope drift; it may be revised through a documented decision and fresh impact assessment.

Do not create ceremony for its own sake. The required depth, disciplines and qualification evidence scale with customer impact, reversibility, dependency surface and uncertainty. Security, tenant isolation, legal/compliance, data integrity and production safety remain non-negotiable regardless of delivery speed.

## Production boundary and release-candidate hygiene

- Accepted ADRs define the delivery architecture. The existence of a `release-*` branch, an open release PR or CI triggers for release branches does **not** by itself redefine the production boundary.
- [`ADR 0005`](Docs/architecture/adr/0005-main-as-production-boundary.md) currently makes `main` the normal application production boundary. Normal delivery remains feature/stack PR → required validation → explicit merge to `main` → production unless a newer accepted ADR replaces that decision.
- A release candidate branch may be used only as an explicit, temporary delivery exception. While it exists, do not generalize its mechanics into permanent architecture or update unrelated guidance as though release branches are the new default.
- A release-candidate PR is a release manifest, not a feature PR. Before it can be considered ready it must reflect the **current** candidate SHA and actual `main...candidate` contents, identify the included issues/PRs or cohesive change groups, record exact-head CI evidence, list every still-required relational/HTTP/Playwright/infrastructure/deployed check, identify database migrations and rollout/rollback dependencies, and call out known exceptions or deferred risks.
- Deterministic CI being green is necessary repository evidence, not a substitute for risk-specific runtime evidence. A green build/test suite does not close an explicitly required SQL Server, HTTP authorization, browser/mobile, infrastructure or deployed smoke gate.
- If a feature PR is merged while its body still says a required gate is pending, treat that as a completion defect: preserve the gap in the release manifest/Linear and resolve or explicitly waive it before production promotion.
- After a temporary release candidate is promoted, retire its delivery artifacts and return new work to the accepted normal flow. Do not roll the release branch forward indefinitely without an explicit architecture decision.
- Temporary validation PRs, branches, workflow edits, generated snapshots and one-off files must be removed or explicitly retained with an owner before the owning issue is completed.

## Canonical local development

- On a supported Windows developer machine, root `./dev.ps1` is the canonical full-stack bootstrap and smoke path. See [`Docs/operations/local-development.md`](Docs/operations/local-development.md).
- Do not invent `appsettings.Local.json` values, copy production configuration, enable remote SQL, or route synthetic test users through Entra merely to make local development start.
- If the canonical bootstrap fails on a clean supported machine, treat that as a Workslip setup defect and fix the maintained bootstrap/configuration instead of documenting tribal workarounds.
- Backend-only/frontend-only manual commands remain valid for focused debugging, but they do not replace the fresh-machine full-stack runtime smoke.

## Delivery loop

For implementation batches, keep the execution loop short and deterministic. Use [`Docs/agents/DELIVERY_HANDOFFS.md`](Docs/agents/DELIVERY_HANDOFFS.md) for role boundaries, the standard handoff, adversarial review and release-readiness decisions.

1. verify the problem against current source/runtime evidence;
2. confirm the owning Linear issue and exact scope for implementation work; do not create a new Linear issue unless explicitly requested;
3. implement the smallest complete correction;
4. add meaningful regression protection for the changed risk;
5. run the required validation from [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md);
6. update the PR body with the validation that actually completed;
7. merge only after the required gate is green or an explicit documented exception exists;
8. update Linear with delivered behaviour and concrete evidence;
9. close superseded/duplicate PRs and remove temporary delivery artifacts before moving on.

Do not leave a merged PR describing validation as `pending` when the result is known. Do not leave abandoned stacked PRs open after an equivalent rebased/sequential PR has replaced them.

After a larger multi-issue batch, perform a short delivery retro: identify throughput wins, mistakes, avoidable ceremony, unvalidated risk, open operational gaps and one or two concrete process improvements. Record durable process decisions in the repository or Linear rather than relying on chat history.

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

Report evidence precisely: static review, build, Unit, Postman feature/API, Playwright, narrow provider-specific evidence, deployed smoke and compliance/operational evidence are different things. Do not say “done”, “works” or “validated” without stating what actually ran and what remains unverified.

Before calling implementation complete, confirm all of the following that apply:

- the final PR body reflects completed CI/test/browser/deployment evidence rather than planned evidence;
- Linear status and delivery notes match what was actually merged;
- superseded or duplicate PRs are closed with a pointer to the replacement;
- known validation gaps are named explicitly, especially missing Postman runtime evidence for backend feature boundaries or Playwright/browser evidence for user-visible critical flows;
- deployment status is checked when deployment is part of the requested outcome.

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
