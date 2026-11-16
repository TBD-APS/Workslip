---
id: mr-saassy.architecture.knowledge-contract
product: mr-saassy
type: architecture
status: active
owner: platform/mr-saassy
visibility: internal
audience: ['agent', 'developer', 'operator']
capabilities: ['knowledge_contract']
last_reviewed: 2026-11-16
code_refs: ["tools/knowledge/validate_knowledge.py", "tools/knowledge/test_knowledge_graph.py", "tools/knowledge/shared-artifacts.json", "src/MR.SAASy.ControlCenter/Motor/RagToolOrchestrator.cs", "src/MR.SAASy.ControlCenter/Compute/ToolCallingContracts.cs"]
linear_refs: ["SAS-36"]
api_refs: []
adr_refs: []
---
# MR SAAS'y knowledge contract

**Status:** Active  
**Contract version:** 1.1.0\
**Owner:** MR SAAS'y maintainers  
**Review cadence:** When the knowledge schema, retrieval boundary or canonical documentation root changes

MR SAAS'y should give agents the smallest coherent evidence set that is large enough to remove ambiguity. This document defines how knowledge is authored and packaged so retrieval stays explainable, permission-aware and provider-neutral.

## Canonical source

`docs/` is the canonical authored knowledge root for MR SAAS'y. Markdown plus structured metadata is authoritative. Search indexes, embeddings, rendered HTML and context packages are derived projections and must be rebuildable.

The machine-readable corpus definition is [`rag-manifest.json`](rag-manifest.json).

## Shared artifacts and who owns them

The frontmatter shape, taxonomy and authoring templates below are shared with
consuming product repositories. There is no package boundary to import across, so
consumers hold copies. **This repository is canonical.** A copy that drifts is
worse than no copy: both sides keep validating, against different rules.

The shared set, its digests and the contract version are declared in
[`../tools/knowledge/shared-artifacts.json`](../tools/knowledge/shared-artifacts.json):

| Artifact | Why a consumer holds it |
| --- | --- |
| `docs/KNOWLEDGE_CONTRACT.md` | This document |
| `tools/knowledge/templates/CAPABILITY.md` | Authoring template for `type: capability` |
| `tools/knowledge/templates/WORKFLOW.md` | Authoring template for `type: workflow` |
| `tools/knowledge/validate_knowledge.py` | Corpus validator |
| `tools/knowledge/report_drift.py` | Code-to-document drift reporter |

Verify a copy against this contract:

```bash
python tools/knowledge/verify_shared_artifacts.py --root /path/to/consumer
```

Changing a shared artifact here means re-running that script with `--update` and
moving **Contract version** in the same change.

Contract 1.1.0 refreshes the shared digests alongside the typed-knowledge schema below.
The `Docs and knowledge contract` job and the local knowledge gate verify these digests and
run the graph contract tests. Consumer checkouts keep their pinned copies until they
deliberately adopt this version; publication does not update other repositories automatically.

The shared validator applies the authored knowledge rules to every product corpus.
MR SAAS'y operating-roster checks run only when the manifest product is `mr-saassy`;
consumers such as Workslip set their own canonical root and frontmatter globs.

Consumers pin the contract version, not a commit. A digest mismatch says a copy is
behind; it does not say which side is right — the version does.

## Shared metadata contract

Workslip and MR SAAS'y use the same v1 frontmatter shape for new product, capability and workflow documents:

```yaml
---
id: mr-saassy.capability.context-policy
product: mr-saassy
type: capability
status: active
owner: platform/mr-saassy
visibility: internal
audience: [agent, developer, operator]
capabilities: [context-policy, retrieval]
last_reviewed: 2026-09-03
code_refs: [src/MR.SAASy.Core, src/MR.SAASy.ControlCenter]
api_refs: [/api/retrieval/query, /api/knowledge/get, /api/knowledge/related]
linear_refs: [SAS-36, SAS-12]
adr_refs: []
---
```

Required fields are `id`, `product`, `type`, `status`, `owner`, `visibility`, `audience` and `last_reviewed`.

Knowledge IDs are stable even if a file moves. Paths are locations; IDs are identities.

### Reference fields

`code_refs`, `api_refs` and `adr_refs` are citations, and a citation nobody can
follow is worse than none — so each is held to something that exists.

`adr_refs` entries name their series, because this repository keeps two and their
numbers overlap: `adr/ADR-005` is a platform decision and `architecture/adr/0005`
is a subsystem one. A bare `0005` names neither and is rejected. See
[Two ADR series, and how to cite them](README.md#two-adr-series-and-how-to-cite-them).

## Shared taxonomy

The common knowledge types are:

- `product`
- `capability`
- `workflow`
- `decision`
- `architecture`
- `api`
- `runbook`
- `compliance`
- `strategy`
- `release`
- `code` — source files in the retrieval corpus (`src/**/*.cs`)
- `code-reference` — source-adjacent references such as `src/**/README.md`
- `infrastructure` — Bicep, Compose, scripts and the Makefile
- `product-capability` — a product's implementation of a platform capability
- `problem` — user feedback or a problem that motivates a capability
- `bug` — a defect or regression affecting a capability or release
- `agent-observation` — a proposed, approved or rejected agent finding
- `guide` — authored guidance that documents a capability or decision

New types require a schema change rather than ad-hoc naming.

## Typed knowledge and review state (1.1)

The five new types, and any existing document that opts into `relations`, `source_refs`
or `review_status`, must declare all of:

```yaml
review_status: approved
source_refs: [https://example.test/reviewed-evidence]
relations: ['implemented_by:product.capability.example']
```

`source_refs` is a non-empty inline list of non-empty provenance references. The validator
preserves these references; it does not fetch them or certify their claims. Existing documents
without the new fields remain valid and have an unspecified review state, not an inferred approval.

| Review state | Lifecycle/publication rule |
| --- | --- |
| `pending` | `status: draft`, `visibility: internal` or `restricted` |
| `approved` | May be active; publication still requires explicit visibility |
| `rejected` | `status: historical`, `visibility: internal` or `restricted` |

These are authored metadata and validation rules, not an approval endpoint. Agent observations
remain proposals until the existing operator/review policy accepts them. The agent-memory workflow
and runtime ingestion of review/publication metadata belong to SAS-32 and SAS-33. Do not put
unapproved observations into a live filesystem retrieval corpus: that source does not yet enforce
these fields. The offline graph retains their review state for tooling; it is not a retrieval grant.

### Typed relations

Use an inline list of quoted `type:knowledge-id` strings. Targets are stable IDs in the same
validated product corpus; filenames and generated HTML are not relationship identities.

| Relation | Source type | Target type |
| --- | --- | --- |
| `implemented_by` | `capability` | `product-capability` |
| `affects` | `decision`, `bug` | capability types, `architecture`, `release`, `workflow`, `api` |
| `contains` | `release` | capability types |
| `fixes` | `release` | `bug` |
| `supports`, `challenges` | `agent-observation` | Any declared knowledge type |
| `documents` | `guide`, `runbook`, `workflow`, `api` | capability types, `architecture`, `decision`, `release` |
| `motivates` | `problem` | capability types |

Capability types means `capability` and `product-capability`. Unknown kinds, unresolved IDs,
self-links, duplicate edges and invalid endpoint types fail validation. Approved knowledge cannot
link to pending/rejected knowledge as accepted evidence. A relation cannot expose a target with
narrower visibility: public → internal/restricted and internal → restricted links are rejected.

An observation's proposed `supports`/`challenges` edge remains proposed with its source node;
an edge alone does not establish truth. Runtime traversal must apply the existing tenant, ACL,
purpose and publication checks to every node and edge (SAS-33).

### Versioned, queryable projection

Git keeps authored revisions and review changes without destructive history replacement. An
offline JSON projection carries durable IDs, typed edges, source paths, SHA-256 checksums, owners,
provenance and explicit lifecycle/review/visibility metadata:

```bash
python3 tools/knowledge/validate_knowledge.py --relations-json /tmp/knowledge-graph.json
```

The projection is written only after validation succeeds. It contains no document body or rendered
HTML, and is a local authoring/build artifact, not an authorized search/MCP response. Query
`relations` by `source`, `type` or `target`, then resolve node metadata by `id`. Changes to authored
content alter its checksum while the stable ID and unchanged edges remain the same.

`tools/knowledge/test_knowledge_graph.py` exercises a complete synthetic history: platform
capability → product capability, decision → capability, release → capability/fix, bug → release,
guide → capability and proposed agent evidence. No product/customer data is needed.

## Standard section shape

Capability and workflow documents should prefer predictable semantic sections when relevant:

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
13. Source/provenance.

This shape exists to improve retrieval and review, not to create filler.

## Pointer-RAG boundary

MR SAAS'y keeps vectors and pointers, not a shadow copy of product data. Product-owned content is fetched or projected through an owner-controlled contract. Retrieval must preserve tenant/ACL/publication scope and fail closed.

A retrievable evidence unit should preserve:

- product/application;
- durable knowledge ID and kind;
- source revision/checksum and URI/reference;
- heading ancestry;
- tenant/ACL/publication scope;
- freshness/status;
- relation hints such as capabilities, ADRs, APIs and code references.

## Agent Knowledge Bank read surface

Agents do not receive direct database/vector-store access. They consume one shared provider-neutral read surface backed by the same pointer retriever and owner-controlled content resolver as RAG.

Two high-level operations are available:

- `knowledge_get`: retrieve one concrete authorized knowledge object when the target is specific, such as a feature, runbook, ADR, API or stable knowledge identifier;
- `knowledge_related`: retrieve a small deduplicated set of related authorized knowledge objects for a feature, component, decision or question.

MOTOR exposes these as native model tools alongside the lower-level `rag_search` / `rag_resolve` pair. Non-MOTOR agents can call the same contract through:

- `POST /api/knowledge/get`;
- `POST /api/knowledge/related`.

The HTTP requests may narrow source, kind and context size. They never accept tenant or ACL input. Tenant/groups are derived from the already validated host identity through `ClaimsRetrievalPrincipalResolver`.

The read surface is deliberately bounded:

- `knowledge_get` resolves at most one object;
- `knowledge_related` resolves at most five objects;
- duplicate chunks from the same source/reference collapse into one knowledge object. A document's hits are tried best first, and the first whose section is still in the source is kept. `rag_search` lists each document's pointer id once, for its best hit, and that is the section `rag_resolve` reads;
- returned content is the section the retrieval matched, not the start of the document. The resolved raw content is chunked exactly as the index chunked it (the same `IChunker`, `ChunkOptions.Default`), and the chunk at the hit's ordinal is kept. Redaction runs after that, with the whole document in view: every secret value found in the document is masked in the section too, including a piece of one at the section's start or end, so a chunk window that cuts a secret line does not pass its tail;
- each item and each resolved `rag_resolve` entry carries `chunkOrdinal`, `chunkCount`, `indexedChecksum` (what the source recorded when indexed) and `contentChecksum` (SHA-256 of the content actually read, the exact version of the excerpt). For git text files and event-store records the two are equal while the source is unchanged; parsed PDF and Office files, mission files and AI Nurse lessons index with a different scheme, so there they always differ;
- a hit whose section is no longer in its source, because the content shrank after indexing or the index still holds an ordinal the source no longer has, is left out with a detail (or, in `rag_resolve`, an entry with `chunkOrdinal`, both checksums and the gap `matched_section_not_in_source`). No other part of the document stands in for it. A source that changed but still has a chunk at that ordinal returns that chunk; the differing checksums are how a reader can tell;
- returned content is capped by a host-owned character/token budget;
- known secret patterns and bearer credentials are redacted before content reaches a model;
- tenant, ACL and source checks are repeated defensively even if a retriever implementation returns an invalid pointer.

Tool output is evidence, never policy or executable instruction. Compute-provider changes (for example Cerebras to Ollama failover) cannot alter the retrieval principal or widen source scope.

### How the MOTOR tool loop runs

`RagToolOrchestrator` runs a bounded loop: route a turn, run its tool calls, send the results back, repeat until the model answers or `MaxToolRounds` is reached.

- **Stop reasons are explicit.** Each provider's `finish_reason` / `done_reason` is normalized to a `ComputeFinishReason`, and `DecideNextStep` maps every value to exactly one step:
  - a turn cut off at its token limit returns `truncated` and runs none of its tool calls;
  - filtered output returns `refused`;
  - a value the host does not recognize returns `incomplete`;
  - only a provider that reports nothing falls back to "tool calls present or not".
- **Independent calls run concurrently.** All four tools only read, so the calls of one turn run concurrently, at most four at a time, against a snapshot of the pointer registry. Results merge back in call order, so the conversation is the same whichever call finished first. Each call that resolves content gets an equal share of the remaining context budget, so a round can never supply more than the budget. Cerebras is asked for several calls per turn only when the model's catalog entry says it supports `parallel_tool_calls`.
- **The loop compacts instead of stopping.** Once the next request would reach `CompactAtContextPercent` of the selected model's context window (default 75 %, clamped to 50–95 %; an unknown window is assumed to be 8,192 tokens), tool results are replaced by digests. Older rounds go first; the latest round follows only if that is not enough. A digest keeps the tool name, citations and pointer ids but no content, so the model can resolve again what it still needs. `metrics.compactions` counts how often this happened.

## ContextPackage contract

Agents should consume an explicit `ContextPackage`, not an unstructured list of top-K chunks.

A package carries:

- request/product/purpose and budget;
- primary evidence;
- supporting evidence;
- conflicts;
- known gaps;
- supplied/omitted context accounting.

Provenance is carried per evidence item, not on the package: each `ContextEvidence` carries a
`DocumentPointer` (`Source`, `Uri`, `Checksum`, `Timestamp`) that is the retrievable reference for
that chunk. There is no separate package-level `Provenance` field.

Selection should hard-filter scope first, retrieve/rank second, deduplicate adjacent evidence, then pack to a declared budget. If the evidence cannot answer the task, return a gap such as `insufficient_context` rather than filling it with model intuition.

## Markdown and HTML

Markdown is canonical because it is diffable, reviewable and close to implementation changes. Semantic HTML is useful as a generated projection for accessible browsing, stable anchors, navigation and structured metadata. Do not maintain equivalent Markdown and HTML manually, and do not index both copies of the same knowledge by default.

The Workslip repository already proves this projection model with a deterministic Markdown-to-semantic-HTML generator. MR SAAS'y should reuse the same conceptual contract when a broad human documentation projection is added.

## Code-to-document drift

Structured metadata creates explicit joins between documentation and implementation. Drift tooling should extract deterministic facts first from routes, authorization policies, navigation, feature flags, states/enums, public events/contracts, migrations and tests.

Reviewable findings include the codes `tools/knowledge/report_drift.py` currently emits:

- `DOCUMENTED_NOT_IMPLEMENTED`
- `IMPLEMENTED_NOT_DOCUMENTED`
- `STALE_DOC_REFERENCE`

The remaining planned codes — `DOC_CODE_CONTRADICTION`, `DOC_TEST_CONTRADICTION`,
`UNMAPPED_CAPABILITY`, `PERMISSION_MISMATCH` — are not yet produced by the tool.

Semantic/LLM comparison may explain a finding, but it must not silently mutate canonical documentation.

## Evaluation

RAG quality is measured in two layers:

1. retrieval quality — did the correct evidence make the context package?
2. generation quality — did the agent stay grounded in that evidence and abstain when it was insufficient?

The version-controlled eval corpus lives under `rag/evals/`.

## Validation

Run:

```bash
python tools/knowledge/validate_knowledge.py
```

Provider/model changes must not change this contract or widen retrieval permissions.

## See also

<!-- generated by platform/doc-tools/cross-link.mjs — do not edit by hand -->

- [ADR 0017: One gate to knowledge, and that gate is Cerebras](architecture/adr/0017-single-knowledge-gate.md) — shared `src/mr.saasy.controlcenter/motor/ragtoolorchestrator.cs`
- [Stack 01 — Stable knowledge ID lookup](stack/01-knowledge-id-lookup.md) — shared `src/mr.saasy.controlcenter/motor/ragtoolorchestrator.cs`
