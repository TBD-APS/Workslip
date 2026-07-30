# Workslip documentation instructions

Read the root `AGENTS.md`, `Docs/agents/OPERATING_CONTRACT.md`, and `Docs/agents/VALIDATION.md` before changing maintained documentation.

## Scope

These rules apply to maintained files under `Docs/`.

## Documentation truth

- Current implementation, executable tests, runtime configuration, active ADRs, and verified deployment behavior take precedence over historical plans.
- Never describe proposed, experimental, inferred, or unexecuted behavior as implemented.
- Mark historical, draft, planned, deprecated, and needs-verification material clearly.
- Prefer updating an existing maintained document over creating a competing document.
- Each maintained document should state status, owner, source of truth, and review cadence where practical.

## Required updates

Changes to these areas must update relevant maintained documentation in the same PR or include an explicit waiver with owner and expiry:

- API contracts and authentication;
- tenant boundaries and dataflows;
- database/schema behavior;
- infrastructure and deployment;
- external integrations;
- release workflows;
- critical user flows;
- significant architecture decisions.

Record significant architecture decisions as ADRs. Record issue-specific implementation details in Linear or the PR rather than creating permanent architecture documentation for temporary details.

## Generated material

Do not hand-edit generated documentation, OpenAPI clients, or generated Postman material. Update the source and run the established generator.

If generation cannot run, document the artifact as stale and list the exact regeneration command. Do not claim consistency.

## Links and duplication

- Use repository-relative links for repository files.
- Remove or consolidate duplicated active guidance.
- Do not let historical plans override active documentation.
- Link ADRs and Linear issues where they explain a non-obvious decision.
- Do not embed secrets, private URLs, personal data, or environment credentials.

## Validation

For documentation-only changes:

- statically review links, paths, headings, and scope;
- run the repository documentation checks when a local checkout is available;
- verify referenced files exist;
- review the diff for accidental replacement of current truth with planned behavior.

Documentation about testing must match `Docs/agents/VALIDATION.md`. Documentation about implementation workflow must match `Docs/agents/OPERATING_CONTRACT.md`.
