# Workslip AI pull-request review

**Status:** Release candidate. GitHub Models is the default provider and uses the job-scoped `GITHUB_TOKEN`; no separate model API key or personal review PAT is required for the baseline review path. Ollama is supported as an optional independent reviewer through Ollama Cloud in GitHub Actions, and the same provider client can be run manually against a local Ollama instance.

This automation adds an advisory review signal after Workslip's normal `CI` succeeds. It does not replace CI, CodeQL, human review, browser/runtime evidence or the repository owner's explicit merge decision.

## Review model

1. The existing `CI` workflow validates the PR first.
2. `AI PR Review` runs automatically after successful PR CI only for `OWNER`, `MEMBER` or `COLLABORATOR` authors. External-contributor PRs require explicit `workflow_dispatch`.
3. Review jobs check out trusted `main` only. They never checkout or execute the pull-request head.
4. PR metadata and the unified diff are fetched through the GitHub API, size-bounded, redacted and marked as untrusted data. A stale CI completion is skipped when the PR has already moved to another head SHA.
5. GitHub Models is always attempted with the job-scoped `GITHUB_TOKEN` and `models: read`. OpenAI Codex and Claude are optional additional independent providers when `OPENAI_API_KEY` or `ANTHROPIC_API_KEY` are configured. Ollama Cloud is optional when `OLLAMA_API_KEY` is configured.
6. The Ollama GitHub Actions job stays on GitHub-hosted `ubuntu-latest`. Workslip does not attach a persistent self-hosted runner to this public repository.
7. The aggregator updates one sticky PR comment. If `WORKSLIP_REVIEW_PAT` is configured, the configured `rasm105k` identity is verified and used; otherwise the trusted aggregate job posts as `github-actions[bot]`.
8. The aggregator publishes an `AI PR Review` commit status on the exact reviewed PR head SHA.
9. A blocking AI status requires at least two independent available providers to report a matching `high` or `critical` finding with confidence >= 0.80. A single-provider review remains advisory.
10. No AI path approves or merges a pull request.

## Optional repository secrets

The baseline path requires no repository secret beyond GitHub's automatically issued `GITHUB_TOKEN`.

Optional integrations:

- `OPENAI_API_KEY` — enables OpenAI Codex as an additional independent reviewer.
- `ANTHROPIC_API_KEY` — enables Claude as an additional independent reviewer.
- `OLLAMA_API_KEY` — enables Ollama Cloud as an additional independent reviewer after the applicable vendor/data/AI-governance approval has been completed.
- `WORKSLIP_REVIEW_PAT` — optional fine-grained personal token for posting the sticky review comment as `rasm105k` instead of `github-actions[bot]`. Keep it restricted to this repository and the minimum PR/comment permission required by GitHub.

Missing optional credentials must never make an otherwise valid review fail. A red/error review status is reserved for a real review failure: no provider available, review publishing failure, or a consensus blocker.

## Ollama GitHub Actions setup

The hosted Ollama provider is disabled automatically while `OLLAMA_API_KEY` is absent.

1. Complete the applicable AI/vendor/data-processing approval under `Docs/compliance/GDPR_AI_ACT_BASELINE.md` before enabling the external provider.
2. Create an Ollama API key and store it as repository secret `OLLAMA_API_KEY`.
3. The workflow defaults `OLLAMA_BASE_URL` to `https://ollama.com` and `OLLAMA_REVIEW_MODEL` to `qwen3-coder:480b`. Both can be overridden with repository variables.
4. Run `AI PR Review` manually against a known PR first and confirm that the sticky comment lists `Ollama` under available providers before relying on it for consensus.

Ollama Cloud does not currently provide Ollama's JSON-schema structured-output mode. The provider therefore includes the existing Workslip schema in the trusted prompt, requests one JSON object, parses it, and then passes it through the same Workslip normalizer used by the other providers. Invalid output degrades that provider instead of weakening the review gate.

## Manual local Ollama mode

The provider client still supports local Ollama without an API key. This mode is intentionally **not** wired to a GitHub self-hosted runner because Workslip is a public repository.

To use the provider locally, generate the normal `.ai-review/review-context.md` through the maintained review-context flow, then run:

```bash
OLLAMA_BASE_URL=http://127.0.0.1:11434 \
OLLAMA_MODEL=qwen3-coder:30b \
node .github/ai-review/ollama-review.mjs
```

Local Ollama uses the JSON schema directly through `format` and does not require authentication on localhost. Keep the local service bound to the developer machine unless a separately secured internal architecture is approved.

## Why there is no public-repo self-hosted Ollama runner

GitHub recommends self-hosted runners only with private repositories because pull requests against a public repository can execute dangerous workflow code and may persistently compromise the runner machine. Workslip therefore keeps its automated Ollama provider on an ephemeral GitHub-hosted runner and uses the authenticated Ollama Cloud API. If the repository later becomes private, or a separately isolated private review-broker architecture is introduced, a local automatic Ollama runner can be reconsidered as a separate security decision.

## Security boundary

`workflow_run` is used deliberately. Review jobs run from the trusted default-branch workflow definition after normal PR CI has completed. Contributor-controlled PR code is never checked out or executed in those jobs.

The untrusted PR title/body/diff can contain prompt-injection text. The review prompt explicitly treats all PR content as data. Provider jobs have read-only repository/PR access; the GitHub Models job additionally has only `models: read`. Model actions are pinned to reviewed commit SHAs. Codex uses a read-only permission profile with sudo dropped; Claude receives only read/search tools plus the workflow's explicit read-only `GITHUB_TOKEN`. Ollama receives the same sanitized review context and has no GitHub write token.

Only the trusted aggregate job receives `pull-requests: write` and `statuses: write`. It never receives `contents: write` or `id-token: write`.

Diffs larger than 420 KB are truncated. A truncated review can report advisory findings but can never create a consensus blocker.

## Compliance boundary

Ollama here is developer/delivery tooling, not a Workslip product AI feature. Its intended input is source-code diff and repository metadata only. It must not receive production customer data, credentials, incident material, support exports or other personal/confidential operational data.

Enabling `OLLAMA_API_KEY` causes the sanitized review context to be sent to Ollama Cloud and therefore introduces an external AI service into the delivery process. Engineering must not treat the existence of this integration as approval to enable it. Vendor role, processing purpose, data categories, terms, retention, training/secondary use, support location and transfers must be reviewed as required by the compliance baseline before the secret is configured.

Human review remains mandatory. Ollama output is advisory unless it independently matches another provider's high-confidence high/critical finding under the existing consensus rule.

## Cost and throughput

AI review starts only after CI is green. Automatic review is restricted to invited/trusted contributor associations, and concurrency is per PR so a newer review cancels an older in-progress review.

The sticky comment is updated on each reviewed SHA rather than adding a new conversation comment for every push.

Ollama Cloud usage and limits depend on the account/model configured there. Local manual inference consumes only the capacity of the developer machine.

## Rollout

Keep `AI PR Review` advisory until one real GitHub Models run has completed end to end from the trusted default-branch workflow. Do not add `OLLAMA_API_KEY` until the compliance/vendor enablement gate is satisfied. After enablement, run one manual end-to-end Ollama review before relying on Ollama for consensus. Ruleset enforcement is a separate decision and must not be combined with provider rollout.
