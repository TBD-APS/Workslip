# Workslip implementation-agent operating contract

**Status:** Active
**Owner:** Workslip maintainers
**Source of truth:** Current repository, applicable law, active ADRs, maintained architecture and compliance documentation, Linear, signed contracts where applicable, and executable validation evidence
**Review cadence:** When agent workflow, repository structure, legal obligations, or delivery expectations change

## Responsibility split

The product owner defines:

- required functionality and user-visible outcome;
- business constraints and priority;
- explicit scope and exclusions;
- compatibility requirements;
- material product and irreversible data decisions;
- accountable owners for legal-role, lawful-basis, retention, DPIA, transfer, AI-risk, and compliance-claim decisions.

The implementation agent owns:

- repository inspection and technical design;
- architecture, maintainability, security, privacy engineering, and scalability;
- complete implementation across affected layers;
- meaningful tests and executable validation;
- documentation, branch hygiene, and pull-request quality;
- identifying personal-data and AI-system impact before implementation;
- enforcing the technical gates in [`../compliance/GDPR_AI_ACT_BASELINE.md`](../compliance/GDPR_AI_ACT_BASELINE.md);
- surfacing verified bugs, compliance gaps, and important risks without waiting for prompting.

Do not ask the product owner to choose class names, folder placement, repository patterns, transaction design, validation libraries, test structure, cache internals, or error-mapping conventions.

Do ask for an accountable decision when implementation depends on legal role, lawful basis, retention semantics, data-subject rights, processor/transfer approval, DPIA outcome, AI Act classification, human-oversight policy, or a public compliance claim. Engineering may identify and recommend the decision but must not silently invent it.

## Source-of-truth order

Before answering repository questions or changing code, use:

1. applicable law and binding regulatory decisions for legal requirements; current code, applicable scoped `AGENTS.md` files, executable tests, database mappings, and runtime configuration for implemented technical behaviour;
2. active ADRs and maintained architecture and compliance documentation;
3. signed processor, vendor, transfer, and customer terms where they control the processing relationship;
4. Linear for scope, priority, acceptance criteria, ownership, and status;
5. current repomix output and generated contracts where applicable;
6. historical plans and specifications as context only.

OpenAPI is the API contract source when it matches running endpoint registrations. Postman is verification material, not a competing contract. Repository documentation is not a substitute for qualified legal approval where interpretation is required.

## Required lookup order

1. Inspect the current branch, worktree, base branch, divergence, and changed files.
2. Read the relevant Linear issue and all applicable scoped instructions.
3. Read [`VALIDATION.md`](VALIDATION.md) and [`../compliance/GDPR_AI_ACT_BASELINE.md`](../compliance/GDPR_AI_ACT_BASELINE.md).
4. Inspect existing implementation patterns with repomix, kioki, `rg`, and repository search.
5. Inspect database/schema sources before reasoning about EF mappings, constraints, migrations, seed data, SQL behavior, data lifecycle, retention, or deletion.
6. Inspect runtime dataflows, vendors, telemetry, caches, files, backups, and external integrations before reasoning about personal-data or AI impact.
7. Use primary package documentation before changing framework or library behavior.
8. Use current official legal and regulator sources before implementing or documenting GDPR or AI Act requirements.

When a required tool or compliance record is unavailable, state that explicitly. Do not silently replace runtime validation, contract evidence, or legal approval with assumptions.

## Repository state gate

Before editing:

- confirm the current branch is not `main`;
- confirm the branch belongs to one Linear issue;
- inspect branch divergence and existing changed files;
- check for uncommitted changes when a local worktree is available;
- search for conflict markers, secrets, credentials, generated environment values, accidental personal data, production identifiers, and restricted compliance evidence;
- identify affected documentation and generated artifacts;
- identify whether the proposed implementation conflicts with an active ADR or prior product decision;
- classify the change as no personal-data/AI impact, personal-data impact, AI-system impact, or both;
- identify any required legal, privacy, security, vendor, or product approval before writing data or integrating an AI/vendor service.

Stop implementation and repair or report the state when any of these are found:

- direct work on `main`;
- committed merge-conflict markers;
- credentials, tokens, private keys, sensitive configuration, production personal data, or restricted legal/compliance records in source control;
- unrelated Linear issues mixed in one branch or PR;
- architecture explicitly rejected by the product owner;
- a known tenant-isolation, authorization, privacy, or data-leakage violation;
- destructive schema or lifecycle work without a production-data, retention, deletion, backup, and rollback plan;
- a new processor, international transfer, or AI provider receiving personal/confidential data without documented approval;
- an AI capability without inventory, role, risk classification, prohibited-practice screening, and required oversight;
- a branch whose state cannot be understood confidently.

Do not run destructive commands, database writes, migrations, Git resets, force pushes, file deletions, rights-request actions, retention purges, or production-data exports without explicit approval. Work read-only by default during review.

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
- Change unrelated files only when required for compilation, validation, generated artifacts, documentation, compliance evidence, or complete feature behavior.

When existing code is unsafe, non-compliant, or broken, report severity, evidence, affected files/dataflows, recommended correction, and whether regression testing, incident review, or legal/privacy escalation is justified. Do not hide a product or compliance defect by weakening a test.

## Architecture and maintainability

Preserve boundaries between frontend, backend, domain logic, persistence, infrastructure, external integrations, privacy/security controls, and AI/model providers.

Review every meaningful change for:

- architectural drift and hidden coupling;
- duplicated logic, duplicated state, and inconsistent API access;
- dead or unused functionality;
- oversized services or components;
- business logic placed in endpoints or UI components;
- infrastructure concerns leaking into application code;
- frontend authorization being treated as a security boundary;
- privacy or AI policy enforced only in presentation code;
- direct vendor/model calls that bypass approved integration boundaries, logging, redaction, or tenant isolation;
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
- logging of tokens, personal data, AI prompts/outputs, or integration payloads;
- unsafe defaults and fail-open behavior;
- personal data in URLs, metrics dimensions, traces, screenshots, CI artifacts, source control, test fixtures, or support tooling;
- AI prompt injection, data exfiltration, insecure tool use, excessive agency, model/provider changes, and cross-tenant retrieval leakage.

Frontend guards improve UX but never replace backend authorization. Redaction reduces risk but never replaces purpose limitation, access controls, retention, or vendor governance.

## GDPR and EU AI Act compliance

[`../compliance/GDPR_AI_ACT_BASELINE.md`](../compliance/GDPR_AI_ACT_BASELINE.md) is a mandatory engineering gate.

For personal-data changes, the PR must identify or link to:

- processing purpose, necessity, data subjects, and data categories;
- controller/processor role and the owner responsible for lawful-basis approval;
- minimization and privacy-by-default decisions;
- retention, deletion, tenant termination, backup, cache, file, telemetry, and retry semantics;
- data-subject rights and transparency impact;
- processors, subprocessors, locations, transfers, and contractual approval;
- DPIA screening and residual risk;
- security and validation evidence.

For AI-system changes, the PR must identify or link to:

- AI inventory entry, model/provider, purpose, users, affected people, and Workslip's legal role;
- AI Act definition and risk classification, prohibited-practice screening, and application date/guidance relied upon;
- personal/confidential data handling and vendor terms;
- transparency, content marking, human oversight, contestability, fallback, and non-AI path where applicable;
- accuracy, bias, robustness, cybersecurity, monitoring, incident, version-change, and rollback evidence;
- AI literacy requirements for operators and reviewers.

No agent may claim full GDPR or AI Act compliance from code review, a checklist, a security control, or a single test. State exactly what has been implemented and verified and what still requires operational, contractual, or legal evidence.

## Data integrity and failure behavior

Review what happens when:

- persistence succeeds and an external integration fails;
- the external integration succeeds and persistence fails;
- a request is retried;
- concurrent requests target the same state;
- a delete, rights request, retention purge, export, or update completes only partially;
- existing production data violates a proposed constraint, retention rule, or minimization requirement;
- a hosted process restarts during work;
- cancellation or timeout occurs after a side effect;
- a vendor, AI model, identity provider, telemetry service, email provider, or storage service is unavailable or changes behaviour;
- an AI output is unsafe, incorrect, discriminatory, fabricated, or cannot be explained sufficiently for the use case.

Use transactions, execution strategies, concurrency checks, idempotency, compensation, tombstones, deletion ledgers, or human review only where the actual failure and accountability model requires them.

## Scalability and performance

Prevent:

- unbounded queries and full-table loading;
- N+1 calls and unnecessary eager loading;
- missing pagination or filtering;
- cross-tenant scans;
- duplicate frontend requests and duplicated server state;
- tenant-unsafe cache keys;
- large eager bundles for rare features;
- long-running request work when an established background-processing pattern is more appropriate;
- telemetry cardinality that includes personal data or tenant/entity identifiers;
- unbounded AI prompts, outputs, context windows, vector stores, logs, retries, costs, or retained conversation history.

Do not introduce queues, distributed caching, new persistence layers, vector databases, model providers, or scaling infrastructure without a verified bottleneck or concrete expected load and the applicable privacy, vendor, retention, and security review.

## Product-owner interruption policy

Ask for input only when the answer changes:

- functionality or user-visible behavior;
- commercial expectations or legal requirements;
- controller/processor role, lawful basis, consent model, retention period, rights handling, processor/transfer acceptance, DPIA residual risk, or public compliance claim;
- AI purpose, risk appetite, human-oversight policy, significant-decision semantics, non-AI fallback, or acceptable model/provider risk;
- backward compatibility;
- irreversible data semantics;
- whether a workflow is reversible;
- whether availability or strict correctness wins during an external outage;
- whether a major rewrite is an acceptable investment.

Make conservative technical decisions independently in all other cases. When legal approval is missing, implement no data expansion or AI integration and record the blocker rather than guessing.

## Documentation and decisions

- Never describe proposed, experimental, unverified, or legally unapproved behavior as implemented or compliant.
- Prefer updating an active document over creating a competing source.
- API, authentication, infrastructure, dataflow, database, release, critical-flow, personal-data, vendor, or AI changes must update affected documentation in the same PR, or include an explicit waiver with owner and expiry.
- Generated documentation and generated clients must not be hand-edited; update their source and regenerate them.
- Record significant architecture, privacy, security, retention, processor, transfer, and AI-governance decisions as ADRs or in the approved compliance system of record.
- Update repomix through the established process when repository changes make it stale.
- Record important decisions from chat in the repository or Linear.
- Keep personal data, confidential contracts, rights requests, incidents, credentials, and restricted compliance evidence out of public repository content; link safely to the approved restricted record instead.

## Completion language

Use precise evidence categories:

- implemented;
- statically reviewed;
- compiled;
- automated tests passed;
- integration-tested;
- Playwright-tested;
- deployed smoke-tested;
- privacy engineering controls verified;
- compliance documentation updated;
- contractual/legal review pending or approved.

Do not use “done”, “works”, “validated”, “GDPR compliant”, “AI Act compliant”, or equivalent without stating what actually ran, what evidence exists, who approved the legal/operational elements, and what remains open. A runtime feature that has only been inspected is **implemented but unvalidated**. A documented compliance requirement without operational and legal evidence is **a baseline, not proof of compliance**.
