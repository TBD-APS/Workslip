# Workslip knowledge contract

**Status:** Active  
**Adopted contract version:** 1.1.1\
**Owner:** Workslip maintainers  
**Review cadence:** When the knowledge schema, retrieval boundary or canonical documentation root changes

This document defines how Workslip knowledge is authored so humans and MR SAAS'y agents can retrieve a bounded, explainable context without treating the repository as an undifferentiated text dump.

## Canonical source

`Docs/` is the canonical authored knowledge root for Workslip. The lower-case `docs/` tree is not part of the RAG corpus and must be migrated or explicitly retained for a narrow non-canonical purpose before it can become retrievable.

Markdown plus structured metadata is the source of truth. HTML is a generated projection for consumption and navigation; generated HTML must not be indexed alongside its source Markdown by default.

The machine-readable corpus contract is [`rag-manifest.json`](rag-manifest.json).

## Shared version and consumer boundary

Workslip adopts MR SAAS'y knowledge contract **1.1.1** from
[the canonical source at `a6b8e35c`](https://github.com/TBD-APS/mr-saassy/blob/a6b8e35c8e986b723cd60e0ffefe1c668188e569/docs/KNOWLEDGE_CONTRACT.md),
with exact artifact hashes and source revision recorded in
[`../tools/knowledge/shared-artifacts.json`](../tools/knowledge/shared-artifacts.json).
The canonical contract is retained verbatim under
[`../tools/knowledge/shared/KNOWLEDGE_CONTRACT.md`](../tools/knowledge/shared/KNOWLEDGE_CONTRACT.md)
for hash verification. That tooling copy is outside the Workslip corpus; its MR SAAS'y
runtime descriptions are not Workslip implementation claims.

The validator, capability/workflow templates and conservative drift reporter are exact
shared copies. Workslip keeps its own `Docs/` root, metadata-required prefixes and
case-sensitive frontmatter patterns. Platform operating-roster checks run only for a
manifest whose product is `mr-saassy`.

This adoption changes authored metadata and local/CI validation. It does not activate
retrieval, grant product-source access or establish measured retrieval quality.

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

Contract 1.1.1 keeps authoring validation aligned with runtime parsing: duplicate runtime
metadata keys and malformed quoted scalars are rejected. The normalized header ends
within 16,384 UTF-16 characters; relevant raw scalars and list items have a 512-character
limit, the runtime's inline lists have at most 32 items, and IDs/product/relation targets
have at most 160 characters. These field limits apply to runtime metadata, not unrelated
authoring extensions such as `code_refs`. Commas inside quoted provenance references
remain part of the reference. Legacy documents do not acquire an implied review state.

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

The shared 1.1 taxonomy also declares `code`, `code-reference`, `infrastructure`,
`product-capability`, `problem`, `bug`, `agent-observation` and `guide`.
Declaring a type does not include its files in Workslip's documentation-only pilot.

## Review state and typed relations

New `product-capability`, `problem`, `bug`, `agent-observation` and `guide` documents,
and any document using `review_status`, `source_refs` or `relations`, must supply
all three fields. Use `source_refs` as a non-empty inline list of provenance references;
the validator does not fetch those references or certify their claims.

| Review state | Required lifecycle/publication boundary |
| --- | --- |
| `pending` | `status: draft`; internal or restricted visibility. |
| `approved` | May be active; visibility remains explicit. |
| `rejected` | `status: historical`; internal or restricted visibility. |

Existing documents without review metadata have an unspecified review state. Approval
must not be inferred. A document's `approved` metadata records a review decision;
it is not a retrieval grant or evidence that a runtime loaded the document.

Relations use quoted `type:knowledge-id` entries. The shared validator checks declared
relation types, endpoint types, resolvable IDs, duplicates, self-links, review state and
visibility. The relation matrix is defined by the pinned shared contract. Workslip's
pilot currently contains one workflow and no typed edges.

## Bounded internal documentation pilot

[`rag-pilot-manifest.json`](rag-pilot-manifest.json) selects exactly
[`workflows/job-rejection.md`](workflows/job-rejection.md), durable ID
`workslip.workflow.job-rejection`, as source-reviewed documentation for a later explicit runtime mount.
Its source is `workslip-reviewed-docs-pilot`, product `workslip`, tenant `platform`.
The broad authoring manifest is not the runtime pilot allowlist.

The pilot contains authored technical documentation only. Lower-case `docs/`, generated
HTML, source files, customer/job records, private logs and repository-wide contents are
outside it. Code/test references are review citations and do not authorize following
those paths for ingestion. Keep the runtime disabled until independent review has
approved the document and the host is explicitly configured with the bounded mount.

Owner metadata and review state must be checked at indexing and read time by the
consuming host. Workslip's static validator does not prove those runtime checks. A
withdrawn, pending, rejected or missing document must not remain retrievable through
cached content; the consuming runtime owns that enforcement and its evidence.

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

Structured docs should link to implementation through `code_refs`, `api_refs`, capability IDs and tests. Automated drift analysis should use deterministic implementation facts first. The shared reporter currently emits:

- `DOCUMENTED_NOT_IMPLEMENTED`
- `IMPLEMENTED_NOT_DOCUMENTED`
- `STALE_DOC_REFERENCE`
- `DOC_CODE_CONTRADICTION`
- `DOC_TEST_CONTRADICTION`
- `UNMAPPED_CAPABILITY`
- `PERMISSION_MISMATCH`

The four contradiction/mapping codes use explicit `code_claims`, `permission_claims`,
`test_refs` and capability ownership mappings. Comparisons use literal API methods,
simple namespaced C# enums, exact test-method declarations and published OpenAPI
security classification where available. Unknown or ambiguous facts remain unassessed;
test-method presence does not prove a passing test, and OpenAPI security classification
does not prove runtime role authorization. Findings remain report-only and pending review.

The reporter does not compose Workslip's
grouped API routes; an unmatched API citation needs source review and is not proof
that the endpoint is missing.

An LLM may explain or prioritize findings, but it must not silently rewrite canonical docs from semantic guesses.

## Pilot evaluation fixtures

[`../rag/evals/golden-workslip.json`](../rag/evals/golden-workslip.json) defines ten
source-backed acceptance cases for the internal pilot: eight answerable correction
workflow questions and two cases requiring abstention about customer data or deployed
behavior. Expected sources, durable IDs and heading ancestry refer to the reviewed
workflow. These are authored fixtures; they do not establish measured retrieval or
model quality, and the fixture file is outside the pilot's ingestion allowlist.

## Validation

Run the existing documentation truth check and the knowledge contract check:

```bash
python tools/docs/check_docs.py
python tools/knowledge/verify_shared_artifacts.py
python -m unittest discover -s tools/knowledge -p 'test_*.py'
python tools/knowledge/validate_knowledge.py
python tools/knowledge/validate_knowledge.py --manifest Docs/rag-pilot-manifest.json --relations-json /tmp/workslip-knowledge-graph.json
```

The knowledge validator is intentionally deterministic. Semantic correctness still requires review against current code, configuration, tests, accepted ADRs and runtime contracts.
