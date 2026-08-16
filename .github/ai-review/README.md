# Workslip AI pull-request review

**Status:** Active trusted review pipeline. The supported automated provider paths are OpenAI, Claude and Ollama. GitHub Models was retired by GitHub and is no longer part of the active pipeline.

This automation adds an advisory exact-head review signal after Workslip's normal `CI` succeeds. It does not replace CI, CodeQL, human review, browser/runtime evidence or the repository owner's explicit merge decision.

## Review model

1. The existing `CI` workflow validates the PR first.
2. `AI PR Review` runs automatically after successful PR CI only for `OWNER`, `MEMBER` or `COLLABORATOR` authors. External-contributor PRs require explicit `workflow_dispatch`.
3. Review jobs check out trusted `main` only. They never checkout or execute the pull-request head.
4. PR metadata and the unified diff are fetched through the GitHub API, size-bounded, redacted and marked as untrusted data. A stale CI completion is skipped when the PR has already moved to another head SHA.
5. OpenAI, Claude and Ollama run as independent providers only when their provider credential/configuration is available.
6. OpenAI model selection is explicit: the trusted job reads the OpenAI project's `/v1/models` catalog, then selects the first configured candidate that the project can actually access. The Codex action never chooses an implicit default model for Workslip review.
7. The Ollama GitHub Actions job stays on GitHub-hosted `ubuntu-latest`. Workslip does not attach a persistent self-hosted runner to this public repository.
8. The aggregator updates one sticky **bot-owned** PR comment using the aggregate job's scoped `GITHUB_TOKEN`, then publishes an `AI PR Review` commit status on the exact reviewed head SHA.
9. A blocking AI status requires at least two independent available providers to report a matching `high` or `critical` finding with confidence >= 0.80. A single-provider review remains advisory.
10. No AI path approves or merges a pull request.

## Provider configuration

### OpenAI

Repository secret:

- `OPENAI_API_KEY` — server/workflow only.

Repository variables:

- `OPENAI_REVIEW_MODEL` — optional preferred model ID.
- `OPENAI_REVIEW_FALLBACKS` — optional comma-separated preference list. If omitted, the maintained workflow uses its current reviewed fallback list.

The job requests `GET /v1/models` with the configured API key and compares returned model IDs to the preferred/fallback candidates. It does not print the raw model catalog or API key. If none of the configured candidates are available, OpenAI is normalized as unavailable with a specific reason rather than letting the Codex action silently choose a changing default.

### Claude

- `ANTHROPIC_API_KEY` enables Claude as an independent reviewer.

Claude receives trusted policy plus the sanitized review context and only the read/search tools declared by the pinned action configuration.

### Ollama

- `OLLAMA_API_KEY` enables Ollama Cloud after the applicable external-provider/compliance approval.
- `OLLAMA_BASE_URL` defaults to `https://ollama.com` and may be overridden through a repository variable.
- `OLLAMA_REVIEW_MODEL` controls the hosted model selection.

The same provider client can still be run manually against a local Ollama instance. Automated public-repository review does not use a persistent self-hosted runner.

## No personal review token

The active review workflow does **not** use `WORKSLIP_REVIEW_PAT` or another personal token for publishing.

Only the trusted aggregate job gets `pull-requests: write` and `statuses: write`. It publishes with its job-scoped `GITHUB_TOKEN`. `post-review.mjs` only updates a marker comment owned by the authenticated workflow identity; a historical comment created by another identity is never patched with the bot token.

## Failure semantics

Missing optional credentials disable that provider without failing unrelated CI.

A review status becomes error/failure when, for example:

- no provider is configured;
- configured providers exist but none is available;
- review publishing fails;
- two independent available providers produce a matching high-confidence high/critical blocker.

A configured OpenAI key with no accessible candidate model is an explicit unavailable-provider state, not an opaque Codex action failure.

## Security boundary

`workflow_run` is deliberate. Review jobs run from the trusted default-branch workflow definition after normal PR CI has completed. Contributor-controlled PR code is never checked out or executed by provider jobs.

The untrusted PR title/body/diff can contain prompt-injection text. The review prompt treats all PR content as data. Provider jobs have read-only repository/PR access. Model actions are pinned to reviewed commit SHAs. Codex uses a read-only permission profile with sudo dropped; Claude receives only read/search tools plus the workflow's read-only GitHub token; Ollama receives the sanitized review context and no GitHub write token.

Only the aggregate job receives write permission for the PR/status surface. It never receives `contents: write` or `id-token: write`.

Diffs larger than 420 KB are truncated. A truncated review can report advisory findings but can never create a consensus blocker.

## Compliance boundary

Automated review is developer/delivery tooling, not a Workslip product AI feature. Intended input is source-code diff and repository metadata only. It must not receive production customer data, credentials, incident material, support exports or other personal/confidential operational data.

Enabling an external provider remains subject to the applicable vendor/data/AI-governance baseline. Provider availability does not expand the data boundary.

## Validation

Static/self-test coverage lives in `.github/workflows/ai-pr-review-selftest.yml` and verifies:

- trusted-main checkout and no PR-head execution;
- model actions remain pinned;
- OpenAI model selection is explicit and tested;
- retired GitHub Models is absent from the active workflow;
- no personal publishing token is used;
- only the aggregate job has PR write permission;
- supported-provider consensus behavior remains deterministic.

A workflow change is not considered fully proven until it is merged to trusted `main` and a subsequent real PR successfully completes the `workflow_run` review path, because the privileged review workflow intentionally executes the default-branch definition rather than the PR's modified workflow.
