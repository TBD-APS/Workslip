# ADR 0017 — Retrieval and model routing belong to the MR SAAS'y agent runtime

**Status:** Accepted

**Owner:** Workslip architecture owner

**Decision scope:** Where retrieval-augmented context construction and model/provider routing live, and how Workslip consumes them. It does not specify the platform's index format, embedding model, chunking strategy or provider credentials — those are MR SAAS'y's to choose.

## Context

We want retrieval-augmented generation (RAG) so that agent operations — PR review, bounded implementation tasks — can retrieve relevant repository and documentation context instead of assembling it heuristically. Hooking up Cerebras as an inference provider is the immediate motivation.

Three facts constrain where that work goes.

**Neither repository has any of it yet.** There is no embedding, vector, index or retrieval code in Workslip or in MR SAAS'y today. This is a greenfield decision, which is exactly when the boundary is cheap to set and expensive to skip.

**The boundary is already written down.** [Automation ownership boundaries](../github-actions-convergence.md) states the invariant directly: *"Delivery must not know about Kimi, Cerebras or model routing, and AI runtime must not own Azure deployment, migrations or release semantics."* It assigns provider/model discovery and routing, the Cerebras adapter, and prompt/context construction to the MR SAAS'y agent runtime. [ADR 0010](0010-mr-saasy-control-plane-bootstrap-boundary.md) makes that control plane an isolated, extractable service, and [ADR 0015](0015-workslip-module-access-consumer-contract.md) fixes the general shape: Workslip consumes platform contracts, never owns them.

**Workslip is carrying orphaned machinery that pre-dates the boundary.** [`.github/ai-review/`](../../../.github/ai-review/) still contains provider adapters (Grok, Ollama, OpenAI), a review prompt, a result schema, an aggregator and a context builder. The workflows that invoked it were deleted in #926 and #929, and nothing in the repository references the directory today. It is dead code, but it is also the only existing implementation of the context construction we are about to replace — so it is reference material, not merely litter.

Building RAG in Workslip would therefore mean reviving retired product-repo automation, embedding provider identity in the product domain, and contradicting an invariant that is already recorded. We are not going to do that.

## Decision

1. **The agent runtime owns retrieval.** Indexing, embedding, chunking, the vector store, the retriever, and the assembly of retrieved context into a prompt live in MR SAAS'y under `agent-runtime/` (`routing/`, `providers/`, `orchestration/`). Workslip contains no embedding model, no index, no vector store and no retriever.

2. **The agent runtime owns provider routing, including Cerebras.** Cerebras is added as one adapter behind the existing provider/model routing seam. No Workslip file names an inference provider, holds a provider credential, or selects a model. "Hook Cerebras up properly" means adding an adapter in the runtime — not a Workslip integration.

3. **Workslip requests operations, not inference.** The product repository asks for bounded, provider-neutral operations — *review this exact PR head*, *implement this bounded task* — through a thin entrypoint. The request names the work and the exact SHA. It never names a model, a provider, a temperature or a prompt.

4. **The trusted/untrusted split is a hard invariant of retrieval, not a detail of the old implementation.** [`build-context.py`](../../../.github/ai-review/build-context.py) separates repository-owned context (documentation, `AGENTS.md` ancestors, source files) from attacker-controlled context (the PR diff and body) into distinct trusted and untrusted artifacts, and redacts secrets before either is emitted. Any retrieval system must preserve that separation end to end. A single undifferentiated index that returns repository documentation and pull-request prose in one ranked list destroys the prompt-injection boundary, and destroys it silently — retrieval quality metrics will not show it. Retrieved content must carry its trust level through ranking, assembly and prompt construction, and untrusted content must never be promoted to trusted by having scored well.

5. **Retirement follows extraction, not the other way round.** `.github/ai-review/` stays in the tree until the runtime implements the replacement, then is deleted in one commit that names the MR SAAS'y module superseding it. Deleting it first would discard the reference implementation while the replacement is still being written; leaving it indefinitely lets retired automation look supported. Git history is not an adequate substitute here, because the point of retaining it is that a person writing the runtime can read it in place.

## What MR SAAS'y must provide

This is the coordination surface. Workslip cannot proceed past the seam until these exist, and they are the platform's to design:

- a versioned operation contract for the agent operations above, with a pinned contract version;
- a retriever whose results carry an explicit trust level per decision 4;
- a Cerebras provider adapter behind the existing routing seam, with credentials held platform-side;
- a thin GitHub entrypoint that Workslip can invoke without provider knowledge, per the convergence document's rule that a feature must not create a feature-specific workflow.

## Relationship to the knowledge contract

[`Docs/KNOWLEDGE_CONTRACT.md`](../../KNOWLEDGE_CONTRACT.md) is the other half of
this boundary and does not conflict with it. That contract governs what Workslip
*publishes* — how its documentation is structured, what metadata each chunk
carries, and what the corpus guarantees to a consumer. This ADR governs where the
*retriever* lives. Workslip owns the corpus; the agent runtime owns retrieval
over it.

One gap is worth naming because it spans both. The knowledge contract requires
each selected chunk to preserve tenant, ACL and publication scope, which answers
*who may see this*. Decision 4 above is a different question — *may this content
influence instructions* — and access scope does not answer it: a document can be
perfectly in-scope for a tenant and still be attacker-authored. Whichever side
implements ranking must carry both.

## Consequences

- The Cerebras work is unblocked on the platform side and needs no Workslip change to begin.
- Workslip's dependency stays a single versioned seam, consistent with ADR 0010, 0014 and 0015, so extracting the runtime to its own repository remains mechanical.
- The prompt-injection boundary that the retired implementation got right is carried forward explicitly instead of being rediscovered after a retrieval system has already flattened it.
- Workslip keeps dead code in the tree for a bounded period, with a named retirement condition, rather than an unbounded one.
- Product-facing RAG inside Workslip — retrieval over customer documents — is a separate decision this ADR does not make or authorize. [Workslip Docs](../workslip-docs.md) explicitly excludes AI/RAG integration in v1, and any such feature carries GDPR/AI-Act obligations under the [compliance baseline](../../compliance/GDPR_AI_ACT_BASELINE.md).
