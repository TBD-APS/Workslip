# Documentation Steward

**Status:** Dormant — runtime workflow retired during repository workflow cleanup
**Owner:** Workslip maintainers
**Source of truth:** Current pull-request source, checked-in configuration,
executable tests, completed checks and the target documentation file
**Implementation:** [Documentation Steward worker](../../.github/documentation-steward/run.py)
**Runtime:** None currently configured

## Purpose

The Documentation Steward implementation is retained as a bounded repository-maintenance
worker for keeping existing technical documentation aligned with a successful trusted
pull request. It is not currently scheduled or invoked by an active GitHub Actions
workflow. Re-enabling it requires an explicit runtime workflow rather than relying on
obsolete delivery-state automation.

When enabled, the worker classifies whether a PR creates an evidenced documentation
delta. If a direct update is safe, it can update one existing technical Markdown file
on the same PR branch and record the source paths, result and confidence in an upserted
PR comment. A human still reviews and merges the resulting PR.

## Inputs and output

The retained worker expects PR metadata, changed-file names and bounded patches, the
completed check snapshot, a list of permitted existing documents and the selected
target document. Repository text is untrusted data, never instructions. It must not
fetch credentials, customer records or private conversation transcripts as agent
context; normal repository data-hygiene rules apply to every pull-request patch.

Every result is one of:

- `NO_CHANGE` — no direct technical-documentation update is evidenced;
- `UPDATED` — one allowed document was updated on the PR branch;
- `HUMAN_REVIEW` — documentation may be needed, but the material exceeds its authority;
- `BLOCKED` — required trusted context, a configured model or a valid output is unavailable.

The PR comment names the exact source paths that support the outcome. It is not merge
approval, release evidence or a claim that the documentation is complete.

## Write boundary

Any future runtime must preserve the existing boundary: run only after successful CI
for a trusted, same-repository PR targeting `main`, and update only an existing
`Docs/**/*.md` technical document. It must refuse:

- source code, workflows, configuration, tests and generated files;
- new documentation files or a document the PR author has already changed;
- `Docs/architecture/adr/`, `Docs/compliance/`, `Docs/strategy/`, `AGENTS.md`
  and agent-governance documents;
- public-site/customer-facing content, publishing and pull-request merge.

When the change needs one of those materials, contradicts the checked-in source or has
insufficient evidence, the worker returns `HUMAN_REVIEW` or `BLOCKED` instead of
guessing.

## Safety and validation

Deterministic code validates the PR trust boundary, source references, target path,
target existence, Markdown shape and update size before any GitHub write. A future
runtime must use a trusted default-branch workflow definition and must not check out or
execute untrusted PR code.

Documentation changes must rerun ordinary CI, including
`python tools/docs/check_docs.py`; reviewers remain responsible for verifying the
technical claim against its named primary source. The retained worker remains covered
by its local Python policy tests.
