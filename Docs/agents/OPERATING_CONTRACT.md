# Workslip implementation-agent operating contract

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** Current repository, active ADRs, maintained architecture documentation, Linear, and executable validation evidence  
**Review cadence:** When agent workflow, repository structure, or delivery expectations change

## Responsibility split

The product owner defines:

- required functionality and user-visible outcome;
- business constraints and priority;
- explicit scope and exclusions;
- compatibility requirements;
- material product and irreversible data decisions.

The implementation agent owns:

- repository inspection and technical design;
- architecture, maintainability, security, and scalability;
- complete implementation across affected layers;
- meaningful tests and executable validation;
- documentation, branch hygiene, and pull-request quality;
- surfacing verified bugs and important risks without waiting for prompting.

Do not ask the product owner to choose class names, folder placement, repository patterns, transaction design, validation libraries, test structure, cache internals, or error-mapping conventions.

## Source-of-truth order

Before answering repository questions or changing code, use:

1. current code, applicable scoped `AGENTS.md` files, executable tests, database mappings, and runtime configuration;
2. active ADRs and maintained architecture documentation;
3. Linear for scope, priority, acceptance criteria, ownership, and status;
4. current repomix output and generated contracts where applicable;
5. historical plans and specifications as context only.

OpenAPI is the API contract source when it matches running endpoint registrations. Postman is verification material, not a competing contract.

## Required lookup order

1. Inspect the current branch, worktree, base branch, divergence, and changed files.
2. Read the relevant Linear issue and all applicable scoped instructions.
3. Inspect existing implementation patterns with repomix, kioki, `rg`, and repository search.
4. Inspect database/schema sources before reasoning about EF mappings, constraints, migrations, seed data, or SQL behavior.
5. Use primary package documentation before changing framework or library behavior.
6. Read [`VALIDATION.md`](VALIDATION.md) before deciding how the change will be tested.

When a required tool is unavailable, state that explicitly. Do not silently replace runtime validation with assumptions.

## Repository state gate

Before editing:

- confirm the current branch is not `main`;
- confirm the branch belongs to one Linear issue;
- inspect branch divergence and existing changed files;
- check for uncommitted changes when a local worktree is available;
- search for conflict markers, secrets, credentials, generated environment values, and accidental personal data;
- identify affected documentation and generated artifacts;
- identify whether the proposed implementation conflicts with an active ADR or prior product decision.

Stop implementation and repair or report the state when any of these are found:

- direct work on `main`;
- committed merge-conflict markers;
- credentials, tokens, private keys, or sensitive configuration in source control;
- unrelated Linear issues mixed in one branch or PR;
- architecture explicitly rejected by the product owner;
- a known tenant-isolation or authorization violation;
- destructive schema work without a production-data and rollback plan;
- a branch whose state cannot be understood confidently.

Do not run destructive commands, database writes, migrations, Git resets, force pushes, or file deletions without explicit approval. Work read-only by default during review.

## Scope and branch discipline

Each branch and pull request must represent one cohesive Linear issue.

- Branch: `rbj--<issue>-<description>`.
- PR title: `RBJ-<issue>: <description>`.
- Do not push directly to `main`.
- Prefer small, cohesive PRs and squash merging.
- Do not mix unrelated cleanup into feature work.
- Do not introduce speculative abstractions, wrappers, dependencies, or patterns.
- Reuse established components, services, and conventions.
- Prefer the smallest complete implementation, not a small diff that handles only the happy path.
- Change unrelated files only when required for compilation, validation, generated artifacts, documentation, or complete feature behavior.

When existing code is unsafe or broken, report severity, evidence, affected files, recommended correction, and whether regression testing is justified.

## Architecture and maintainability

Preserve boundaries between frontend, backend, domain logic, persistence, infrastructure, and external integrations.

Review every meaningful change for:

- architectural drift and hidden coupling;
- duplicated logic, duplicated state, and inconsistent API access;
- dead or unused functionality;
- oversized services or components;
- business logic placed in endpoints or UI components;
- infrastructure concerns leaking into application code;
- frontend authorization being treated as a security boundary;
- unnecessary abstractions or dependencies.

Do not create an abstraction based only on an anticipated future consumer. Introduce it when a concrete current need justifies the maintenance cost.

## Security and tenant isolation

Review as applicable:

- authentication and authorization;
- tenant isolation and IDOR;
- role escalation and permission revocation;
- token storage, lifetime, replay, and refresh behavior;
- secrets and sensitive-data exposure;
- cache leakage across users or organizations;
- frontend-only enforcement;
- logging of tokens, personal data, or integration payloads;
- unsafe defaults and fail-open behavior.

Frontend guards improve UX but never replace backend authorization.

## Data integrity and failure behavior

Review what happens when:

- persistence succeeds and an external integration fails;
- the external integration succeeds and persistence fails;
- a request is retried;
- concurrent requests target the same state;
- a delete or update completes only partially;
- existing production data violates a proposed constraint;
- a hosted process restarts during work;
- cancellation or timeout occurs after a side effect.

Use transactions, execution strategies, concurrency checks, idempotency, or compensation only where the actual failure mode requires them.

## Scalability and performance

Prevent:

- unbounded queries and full-table loading;
- N+1 calls and unnecessary eager loading;
- missing pagination or filtering;
- cross-tenant scans;
- duplicate frontend requests and duplicated server state;
- tenant-unsafe cache keys;
- large eager bundles for rare features;
- long-running request work when an established background-processing pattern is more appropriate.

Do not introduce queues, distributed caching, new persistence layers, or scaling infrastructure without a verified bottleneck or concrete expected load.

## Product-owner interruption policy

Ask for input only when the answer changes:

- functionality or user-visible behavior;
- commercial expectations or legal requirements;
- backward compatibility;
- irreversible data semantics;
- whether a workflow is reversible;
- whether availability or strict correctness wins during an external outage;
- whether a major rewrite is an acceptable investment.

Make conservative technical decisions independently in all other cases.

## Documentation and decisions

- Never describe proposed, experimental, or unverified behavior as implemented.
- Prefer updating an active document over creating a competing source.
- API, authentication, infrastructure, dataflow, database, release, or critical-flow changes must update affected documentation in the same PR, or include an explicit waiver with owner and expiry.
- Generated documentation and generated clients must not be hand-edited; update their source and regenerate them.
- Record significant architecture decisions as ADRs.
- Update repomix through the established process when repository changes make it stale.
- Record important decisions from chat in the repository or Linear.

## Completion language

Use precise evidence categories:

- implemented;
- statically reviewed;
- compiled;
- automated tests passed;
- integration-tested;
- Playwright-tested;
- deployed smoke-tested.

Do not use “done”, “works”, “validated”, or equivalent without stating what actually ran. A runtime feature that has only been inspected is **implemented but unvalidated**.
