# Shared Agent Handbook

**State:** Active

This is the canonical provider-neutral onboarding contract for agents working on Workslip and the MR SAAS'y control-plane work represented in this repository. Root `AGENTS.md` remains authoritative for repository-wide engineering rules; this handbook explains the common operating context every agent role/provider must load before execution.

## 1. Shared mental model

Agents are workers inside one delivery system, not independent sources of truth.

Separate these concepts:

- **Role** — what the agent is responsible for, for example Implementation, Review, QA, Security, SRE, Architecture or Research.
- **Provider/runtime** — the execution technology, for example Codex/ChatGPT, Claude, Linear Agent or a future provider.
- **Capability** — what evidence/checkpoints the runtime can publish or consume.
- **Task context** — the current Linear issue, repository state, branch/PR/SHA and relevant source material.

Control Center must be able to add a new provider without changing core domain semantics or provider-specific UI. Provider adapters translate provider events into shared contracts.

## 2. Source of truth

Use the repository hierarchy defined by root `AGENTS.md`.

For implemented technical behaviour, prefer current code/configuration/schema/tests, then verified generated/runtime contracts and infrastructure definitions, then accepted ADRs/maintained docs, then Linear for scope/status.

Chats and agent transcripts are workspaces, not durable documentation. Important decisions must leave the conversation and be recorded in repository documentation, an ADR, Linear, or the owning system.

Do not treat generated snapshots, summaries or another agent's prose as stronger evidence than current primary sources.

## 3. Startup protocol

Before changing anything:

1. Resolve the owning Linear issue and its dependencies.
2. Inspect current branch/worktree and active related PRs/stacks.
3. Read root `AGENTS.md` and every scoped `AGENTS.md` applying to touched paths.
4. Read active ADRs and maintained docs relevant to the boundary.
5. Inspect current implementation/tests/configuration before proposing a change.
6. Load only task-relevant context; do not ingest the whole repository or private transcript history by default.
7. Declare role, provider/runtime, capabilities and loaded handbook/source revision in the agent checkpoint when supported.

If the required context cannot be obtained, publish `BLOCKED` or `UNKNOWN`; do not guess.

## 4. Delivery contract

Implementation work follows the repository delivery rules:

- read-only review by default until implementation is requested;
- one cohesive Linear issue per implementation branch/PR;
- branch `rbj--<issue>-<description>`;
- PR `RBJ-<issue>: <description>`;
- no direct pushes to `main` for implementation work;
- prefer small cohesive PRs and Git stacks for ordered/overlapping work;
- squash merge;
- run the risk-appropriate validation from `Docs/agents/VALIDATION.md`;
- record actual evidence, not planned evidence;
- remove temporary workflows/files/branches or assign an explicit owner before completion.

Repository-owner-approved documentation/governance-only edits may use the exception documented in root `AGENTS.md`.

## 5. Architecture contract

All agents preserve these boundaries unless an accepted architecture decision changes them:

- frontend, backend, infrastructure and external integrations have explicit ownership;
- business/security rules live in the appropriate service/domain layer, not hidden in persistence or UI;
- backend authorization and tenant isolation are authoritative;
- reuse established shared components/services/validators/contracts before adding abstractions;
- do not introduce wrappers, dependencies or patterns without a concrete need;
- review transactions, retries, idempotency, concurrency, partial failures, cache isolation and sensitive logging where relevant;
- do not weaken tests/guards to make a change pass;
- provider-specific Control Center payloads remain adapter-owned; core stores normalized projections and evidence references.

## 6. Role boundaries

### Implementation
Own the smallest complete implementation, regression protection, validation and delivery evidence. Do not self-approve risk merely because the code was authored successfully.

### Review
Act independently from the implementation. Prioritize verified correctness, security, data integrity, architectural drift, race/partial-failure risks and maintainability. Do not silently become the implementer unless explicitly delegated.

### QA
Prove user-visible behaviour at the required runtime level. Unit/build success does not replace required browser/mobile/HTTP/relational evidence.

### Security
Review authorization, tenant isolation, secrets, sensitive logging, dependencies, external processors and trust boundaries. Never publish secrets in checkpoints.

### SRE / Operations
Track health, freshness, incidents, automation failures and operational evidence. `UNKNOWN`, `BLOCKED`, `STALE` or missing telemetry must never be reported as healthy.

### Architecture / Technical debt
Use measurable dependency/ownership evidence. A refactor that only moves code while worsening coupling is not an improvement.

### Research / Product
Distinguish evidence, assumptions and recommendations. Research does not override repository technical truth or Linear delivery state.

## 7. Control Center checkpoint contract

When the provider supports checkpoint publishing, emit normalized state rather than provider-specific prose.

Required concepts:

- agent role;
- provider/runtime;
- issue reference;
- PR/branch/SHA/session references when applicable;
- state: `ACTIVE`, `WAITING`, `BLOCKED`, `FAILED`, `COMPLETED`, `STALE`, or `UNKNOWN`;
- current task;
- last meaningful checkpoint;
- blocker when present;
- next action;
- evidence references;
- observation/freshness timestamp.

Do not create fake successful runs merely to represent a provider error. Provider errors are explicit blocked/unknown observations.

## 8. Security, privacy and retention

- Never publish credentials, tokens, private keys or production secrets.
- Minimize personal/customer data in prompts, checkpoints and central read models.
- Do not persist raw private conversations/transcripts centrally by default.
- Prefer sanitized summaries, metadata, checkpoints and references back to the owning provider.
- Respect provider/source ACL and retention semantics.
- Do not escalate permissions autonomously.
- Stop before destructive production operations, irreversible data semantics or unapproved processor/data-transfer decisions.

## 9. Documentation responsibility

An agent is not finished when an important decision exists only in its conversation.

Use:

- **ADR** for significant durable architecture/security/privacy decisions;
- **maintained docs/AGENTS.md** for current operating rules and system boundaries;
- **Linear** for scope, dependencies, status, blockers and delivery evidence;
- **PR** for implementation-specific review/evidence.

Update stale maintained documentation in the same cohesive change when implementation makes it inaccurate. Surface duplicate or conflicting documentation rather than creating another source.

## 10. Machine-readable onboarding reference

Providers should eventually publish this logical manifest through the shared agent/provider contract:

```text
handbookVersion
architectureContractVersion
checkpointContractVersion
agentRole
providerRuntime
capabilities[]
loadedAt
sourceRevision
```

Initial handbook version: `1`.

The canonical `sourceRevision` is the repository revision from which this handbook and applicable agent rules were loaded. Control Center may warn or block according to policy when an active agent is materially stale relative to the current required handbook/contract revision.

## 11. New-agent onboarding checklist

A new provider/agent is plug-and-play only when:

1. its role is declared independently from provider/runtime;
2. it can load this handbook and applicable repository instructions;
3. its capabilities are declared through the shared provider contract;
4. it maps activity/checkpoints to normalized Control Center states;
5. it preserves evidence provenance and links to source systems;
6. it obeys privacy/security defaults;
7. provider failure/staleness is fail-closed, not rendered healthy;
8. adding it requires adapter/configuration work rather than Control Center core or provider-specific UI changes.

The architectural proof for plug-and-play is onboarding an additional provider without changing Control Center core semantics.
