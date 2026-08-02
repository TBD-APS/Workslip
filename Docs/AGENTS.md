# Workslip documentation instructions

Read the root `AGENTS.md`, `Docs/agents/OPERATING_CONTRACT.md`, `Docs/agents/VALIDATION.md`, and `Docs/compliance/GDPR_AI_ACT_BASELINE.md` before changing maintained documentation.

## Scope

These rules apply to maintained files under `Docs/`.

## Documentation truth

- Current implementation, executable tests, runtime configuration, active ADRs, verified deployment behavior, applicable law, signed contracts, and approved compliance records take precedence over historical plans.
- Never describe proposed, experimental, inferred, unexecuted, or legally unverified behavior as implemented or compliant.
- Mark historical, draft, planned, deprecated, needs-verification, and legally pending material clearly.
- Prefer updating an existing maintained document over creating a competing document.
- Each maintained document should state status, owner, source of truth, and review cadence where practical.
- Separate verified technical facts, legal requirements, assumptions, open decisions, and recommendations.
- Do not claim that a control satisfies GDPR or the EU AI Act merely because it appears secure or privacy-friendly; identify the required evidence and accountable approval.

## Required updates

Changes to these areas must update relevant maintained documentation in the same PR or include an explicit waiver with owner and expiry:

- API contracts and authentication;
- tenant boundaries and dataflows;
- database/schema behavior;
- infrastructure and deployment;
- external integrations;
- release workflows;
- critical user flows;
- significant architecture decisions;
- personal-data collection, purpose, legal-role classification, lawful-basis ownership, access, retention, deletion, export, telemetry, caching, backup, processors, subprocessors, or international transfers;
- data-subject rights, privacy notices, breach response, DPIA screening, records of processing, or security controls affecting personal data;
- AI-system inventory, procurement, provider/model changes, role and risk classification, prohibited-practice screening, transparency, human oversight, data governance, AI literacy, monitoring, or AI incidents.

Record significant architecture decisions as ADRs. Record issue-specific implementation details in Linear or the PR rather than creating permanent architecture documentation for temporary details.

The active compliance baseline is `Docs/compliance/GDPR_AI_ACT_BASELINE.md`. Processing registers, retention schedules, vendor registers, DPIAs, transfer assessments, AI inventories, and training records may be stored outside the public repository when they contain confidential or personal data, but the repository must link to the approved system of record and document ownership and review cadence without exposing restricted content.

## Legal and regulatory sources

- Use current official legal texts and regulator guidance as primary sources.
- Record the source, version/date, jurisdiction, and review date for legal requirements that affect implementation.
- Re-check time-sensitive obligations before release, especially AI Act application dates, Commission guidance, standards, and national implementation.
- Do not rely on blog posts, vendor marketing, search snippets, or AI-generated summaries as the sole authority for legal requirements.
- Escalate legal interpretation, controller/processor role decisions, lawful basis, DPIA outcomes, transfer assessments, and compliance claims to the accountable owner or qualified counsel.

## Generated material

Do not hand-edit generated documentation, OpenAPI clients, or generated Postman material. Update the source and run the established generator.

If generation cannot run, document the artifact as stale and list the exact regeneration command. Do not claim consistency.

## Links and duplication

- Use repository-relative links for repository files.
- Remove or consolidate duplicated active guidance.
- Do not let historical plans override active documentation.
- Link ADRs and Linear issues where they explain a non-obvious decision.
- Do not embed secrets, private URLs, personal data, production identifiers, restricted contracts, incident details, rights-request content, or environment credentials.
- Link to restricted compliance records by safe identifier and owner rather than copying sensitive evidence into the public repository.

## Validation

For documentation-only changes:

- statically review links, paths, headings, scope, and source dates;
- run the repository documentation checks when a local checkout is available;
- verify referenced files exist;
- review the diff for accidental replacement of current truth with planned behavior;
- verify legal claims against current official sources and mark legal approval status;
- verify that examples and screenshots contain no personal, confidential, tenant, credential, or production data.

Documentation about testing must match `Docs/agents/VALIDATION.md`. Documentation about implementation workflow must match `Docs/agents/OPERATING_CONTRACT.md`. Documentation about personal data or AI systems must match `Docs/compliance/GDPR_AI_ACT_BASELINE.md`.
