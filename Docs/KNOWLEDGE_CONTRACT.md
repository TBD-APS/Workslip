# Workslip knowledge contract

**Status:** Active  
**Owner:** Workslip maintainers  
**Review cadence:** When the knowledge schema, retrieval boundary or canonical documentation root changes

This document defines how Workslip knowledge is authored so humans and MR SAAS'y agents can retrieve a bounded, explainable context without treating the repository as an undifferentiated text dump.

## Canonical source

`Docs/` is the canonical authored knowledge root for Workslip. The lower-case `docs/` tree is not part of the RAG corpus and must be migrated or explicitly retained for a narrow non-canonical purpose before it can become retrievable.

Markdown plus structured metadata is the source of truth. HTML is a generated projection for consumption and navigation; generated HTML must not be indexed alongside its source Markdown by default.

The machine-readable corpus contract is [`rag-manifest.json`](rag-manifest.json).

## Knowledge identity

New product, capability and workflow documents must start with YAML frontmatter. Keep metadata deliberately small and stable:

```yaml
---
id: workslip.workflow.kls
product: workslip
type: workflow
status: active
owner: product/workslip
visibility: internal
audience: [agent, developer, operator]
capabilities: [kls, jobs]
last_reviewed: 2026-09-03
code_refs: [src/BE/WorkslipApi, src/FE]
api_refs: [/api/worksheets]
linear_refs: [WOR-748]
adr_refs: []
---
```

Required fields for structured knowledge are `id`, `product`, `type`, `status`, `owner`, `visibility`, `audience` and `last_reviewed`.

IDs are durable identifiers. Rename files when useful, but do not silently reuse an ID for different meaning.

## Shared taxonomy

Use the same top-level concepts in Workslip and MR SAAS'y where they apply:

- `product` — product meaning, terminology, roles and permissions.
- `capability` — a durable user/system capability and its ownership.
- `workflow` — actors, states, transitions, invariants and failure paths.
- `decision` — accepted durable architecture/design choice; normally an ADR.
- `architecture` — current system boundaries and dataflows.
- `api` — externally meaningful runtime contract or integration.
- `runbook` — current operational procedure.
- `compliance` — maintained privacy/security/compliance baseline.
- `strategy` — current product/company direction; never runtime truth.
- `release` — generated or curated change history, not implementation truth.

## Standard document shape

Capability and workflow knowledge should be easy to retrieve section-by-section. Prefer this order when relevant:

1. Purpose.
2. Actors and user intent.
3. Preconditions.
4. Happy path.
5. States and transitions.
6. Business rules and invariants.
7. Permissions and tenant boundary.
8. Failure and edge cases.
9. APIs, events and data touched.
10. UI locations.
11. Related capabilities and decisions.
12. Verification and tests.
13. Source/provenance notes.

Do not pad a document merely to satisfy the shape. Omit sections that do not add durable knowledge.

## Retrieval boundary

The retrieval system should prefer a coherent evidence set over raw top-K chunks. Every selected chunk must preserve at least:

- product/application;
- knowledge ID and kind;
- source path/reference and source revision/checksum;
- heading ancestry;
- tenant/ACL/publication scope;
- freshness/status;
- relation hints such as capability, ADR, API and code references.

Agents must distinguish documented facts from inference. Missing evidence is represented as `insufficient_context`; conflicting evidence must be surfaced rather than silently resolved by the model.

## Context package

An agent context package should contain:

- the task/purpose and product scope;
- a short selected-context summary;
- primary evidence;
- supporting evidence;
- conflicts and known gaps;
- source/provenance references;
- requested/supplied token budget and omitted-hit count.

Context budgeting is intentional. More text is not automatically better context.

## Markdown and HTML

Markdown remains canonical because it is reviewable, diffable and close to Git/code changes. Semantic HTML adds value as a generated projection through stable anchors, accessibility, richer navigation and optional structured metadata.

Workslip already has [`../tools/docs/build_agent_docs_html.py`](../tools/docs/build_agent_docs_html.py), which renders a deterministic semantic HTML agent document while explicitly keeping Markdown as source of truth. Reuse or generalize that projection model instead of hand-authoring equivalent HTML.

## Code-to-document drift

Structured docs should link to implementation through `code_refs`, `api_refs`, capability IDs and tests. Automated drift analysis should use deterministic implementation facts first and report reviewable findings such as:

- `DOCUMENTED_NOT_IMPLEMENTED`
- `IMPLEMENTED_NOT_DOCUMENTED`
- `DOC_CODE_CONTRADICTION`
- `DOC_TEST_CONTRADICTION`
- `STALE_DOC_REFERENCE`
- `UNMAPPED_CAPABILITY`
- `PERMISSION_MISMATCH`

An LLM may explain or prioritize findings, but it must not silently rewrite canonical docs from semantic guesses.

## Validation

Run the existing documentation truth check and the knowledge contract check:

```bash
python tools/docs/check_docs.py
python tools/knowledge/validate_knowledge.py
```

The knowledge validator is intentionally deterministic. Semantic correctness still requires review against current code, configuration, tests, accepted ADRs and runtime contracts.
