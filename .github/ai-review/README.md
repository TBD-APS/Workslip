# Workslip AI pull-request review

**Status:** Release candidate. GitHub Models is the default provider and uses the job-scoped `GITHUB_TOKEN`; no separate model API key or personal review PAT is required for the baseline review path. Ollama can be enabled as an additional local/self-hosted provider without sending the review prompt or PR diff to an external model API.

This automation adds an advisory review signal after Workslip's normal `CI` succeeds. It does not replace CI, CodeQL, human review, browser/runtime evidence or the repository owner's explicit merge decision.

## Review model

1. The existing `CI` workflow validates the PR first.
2. `AI PR Review` runs automatically after successful PR CI only for `OWNER`, `MEMBER` or `COLLABORATOR` authors. External-contributor PRs require explicit `workflow_dispatch`.
3. Review jobs check out trusted `main` only. They never checkout or execute the pull-request head.
4. PR metadata and the unified diff are fetched through the GitHub API, size-bounded, redacted and marked as untrusted data. A stale CI completion is skipped when the PR has already moved to another head SHA.
5. GitHub Models is always attempted with the job-scoped `GITHUB_TOKEN` and `models: read`. OpenAI Codex and Claude are optional additional independent providers when `OPENAI_API_KEY` or `ANTHROPIC_API_KEY` are configured. Ollama is an optional independent provider when `OLLAMA_REVIEW_ENABLED=true` is set as a repository variable and an eligible self-hosted runner is online.
6. The Ollama job targets a self-hosted runner carrying the custom `ollama` label. By default it calls `http://127.0.0.1:11434/api/chat` on that runner, so the Ollama service does not need to be exposed to the public internet.
7. The aggregator updates one sticky PR comment. If `WORKSLIP_REVIEW_PAT` is configured, the configured `rasm105k` identity is verified and used; otherwise the trusted aggregate job posts as `github-actions[bot]`.
8. The aggregator publishes an `AI PR Review` commit status on the exact reviewed PR head SHA.
9. A blocking AI status requires at least two independent available providers to report a matching `high` or `critical` finding with confidence >= 0.80. A single-provider review remains advisory.
10. No AI path approves or merges a pull request.

## Optional repository secrets

The baseline path requires no repository secret beyond GitHub's automatically issued `GITHUB_TOKEN`.

Optional integrations:

- `OPENAI_API_KEY` — enables OpenAI Codex as an additional independent reviewer.
- `ANTHROPIC_API_KEY` — enables Claude as an additional independent reviewer.
- `WORKSLIP_REVIEW_PAT` — optional fine-grained personal token for posting the sticky review comment as `rasm105k` instead of `github-actions[bot]`. Keep it restricted to this repository and the minimum PR/comment permission required by GitHub.

Ollama does not require a repository secret for the default localhost configuration.

Missing optional credentials must never make an otherwise valid review fail. A red/error review status is reserved for a real review failure: no provider available, review publishing failure, or a consensus blocker.

## Ollama setup

Ollama is disabled by default so a missing/offline self-hosted runner can never stall ordinary AI review.

1. Install and start Ollama on the machine that will run the GitHub self-hosted runner.
2. Pull the code-review model on that machine. The workflow defaults to `qwen3-coder:30b`; override it with the `OLLAMA_REVIEW_MODEL` repository variable when another locally available model is more appropriate.
3. Register a GitHub Actions self-hosted runner for this repository and add the custom runner label `ollama`.
4. Keep Ollama bound to localhost when the runner and Ollama service share the same machine. The workflow defaults `OLLAMA_BASE_URL` to `http://127.0.0.1:11434`.
5. Set repository variable `OLLAMA_REVIEW_ENABLED` to `true` only after the labelled runner and model are ready.
6. Optional: set `OLLAMA_BASE_URL` and `OLLAMA_REVIEW_MODEL` repository variables to override the defaults.
7. Run `AI PR Review` manually against a known PR first and confirm that the sticky comment lists `Ollama` under available providers before relying on it for consensus.

The runner must have Git, Node.js and Python 3 available because the trusted review workflow uses the existing checkout action, the Node provider/normalizer, and `build-context.py`. Do not attach the `ollama` label to a shared runner that executes untrusted repository code.

## Security boundary

`workflow_run` is used deliberately. Review jobs run from the trusted default-branch workflow definition after normal PR CI has completed. Contributor-controlled PR code is never checked out or executed in those jobs.

The untrusted PR title/body/diff can contain prompt-injection text. The review prompt explicitly treats all PR content as data. Provider jobs have read-only repository/PR access; the GitHub Models job additionally has only `models: read`. Model actions are pinned to reviewed commit SHAs. Codex uses a read-only permission profile with sudo dropped; Claude receives only read/search tools plus the workflow's explicit read-only `GITHUB_TOKEN`. Ollama receives the same sanitized review context and schema through a direct local HTTP call and has no GitHub write token.

Only the trusted aggregate job receives `pull-requests: write` and `statuses: write`. It never receives `contents: write` or `id-token: write`.

Diffs larger than 420 KB are truncated. A truncated review can report advisory findings but can never create a consensus blocker.

## Compliance boundary

Ollama here is developer/delivery tooling, not a Workslip product AI feature. It reviews source-code diffs and repository metadata only. It must not receive production customer data, credentials, incident material, support exports or other personal/confidential operational data. Because the default path is local to the self-hosted runner, enabling it does not by itself add a remote AI processor; changing `OLLAMA_BASE_URL` to a remote service or cloud endpoint requires a fresh processor/data-transfer and AI-governance review under `Docs/compliance/GDPR_AI_ACT_BASELINE.md`.

Human review remains mandatory. Ollama output is advisory unless it independently matches another provider's high-confidence high/critical finding under the existing consensus rule.

## Cost and throughput

AI review starts only after CI is green. Automatic review is restricted to invited/trusted contributor associations, and concurrency is per PR so a newer review cancels an older in-progress review.

The sticky comment is updated on each reviewed SHA rather than adding a new conversation comment for every push.

Ollama inference consumes only the capacity of the selected self-hosted machine; model load time and review latency therefore depend on the configured model and hardware.

## Rollout

Keep `AI PR Review` advisory until one real GitHub Models run has completed end to end from the trusted default-branch workflow. Enable Ollama only after its self-hosted runner has passed one manual end-to-end review with the expected local model. Ruleset enforcement is a separate decision and must not be combined with provider rollout.
