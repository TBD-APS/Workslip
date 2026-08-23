# Documentation Steward

**Status:** Active
**Owner:** Workslip maintainers
**Source of truth:** Current pull-request source, checked-in configuration,
executable tests, completed checks and the target documentation file
**Runtime:** [AI Delivery State workflow](../../.github/workflows/ai-delivery-state.yml)

## Purpose

The Documentation Steward keeps existing technical documentation aligned with a
successful trusted pull request. It is a narrow repository-maintenance worker,
not an author of product strategy, policy or public content.

The worker first classifies whether the PR creates an evidenced documentation
delta. If a direct update is safe, it updates one existing technical Markdown
file on the same PR branch and records the source paths, result and confidence
in an upserted PR comment. A human still reviews and merges the resulting PR.

## Inputs and output

It receives PR metadata, changed-file names and bounded patches, the completed
check snapshot, a list of permitted existing documents and the selected target
document. Repository text is untrusted data, never instructions. The workflow
does not fetch credentials, customer records or private conversation
transcripts as agent context; normal repository data-hygiene rules still apply
to every pull-request patch.

Every result is one of:

- `NO_CHANGE` — no direct technical-documentation update is evidenced;
- `UPDATED` — one allowed document was updated on the PR branch;
- `HUMAN_REVIEW` — documentation may be needed, but the material exceeds its authority;
- `BLOCKED` — required trusted context, a configured Kimi model or a valid output is unavailable.

The PR comment names the exact source paths that support the outcome. It is not
merge approval, release evidence or a claim that the documentation is complete.

## Write boundary

The workflow runs only after successful CI for a trusted, same-repository PR
targeting `main`. It updates only an existing `Docs/**/*.md` technical document.
It refuses:

- source code, workflows, configuration, tests and generated files;
- new documentation files or a document the PR author has already changed;
- `Docs/architecture/adr/`, `Docs/compliance/`, `Docs/strategy/`, `AGENTS.md`
  and agent-governance documents;
- public-site/customer-facing content, publishing and pull-request merge.

When the change needs one of those materials, contradicts the checked-in source
or has insufficient evidence, the worker returns `HUMAN_REVIEW` or `BLOCKED`
instead of guessing.

## Safety and validation

Kimi chooses the documentation delta, but deterministic code validates the PR
trust boundary, source references, target path, target existence, Markdown
shape and update size before GitHub receives a write. It uses a trusted
default-branch workflow definition and never checks out or executes PR code.

The generated documentation commit reruns ordinary CI, including
`python tools/docs/check_docs.py`; reviewers remain responsible for verifying
the technical claim against its named primary source. The worker is covered by
its local Python policy tests and the control-plane routing tests.
