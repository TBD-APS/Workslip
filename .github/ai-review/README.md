# Workslip AI pull-request review

**Status:** Release candidate. GitHub Models is the default provider and uses the job-scoped `GITHUB_TOKEN`; no separate model API key or personal review PAT is required for the baseline review path.

This automation adds an advisory review signal after Workslip's normal `CI` succeeds. It does not replace CI, CodeQL, human review, browser/runtime evidence or the repository owner's explicit merge decision.

## Review model

1. The existing `CI` workflow validates the PR first.
2. `AI PR Review` runs automatically after successful PR CI only for `OWNER`, `MEMBER` or `COLLABORATOR` authors. External-contributor PRs require explicit `workflow_dispatch`.
3. Review jobs check out trusted `main` only. They never checkout or execute the pull-request head.
4. PR metadata and the unified diff are fetched through the GitHub API, size-bounded, redacted and marked as untrusted data. A stale CI completion is skipped when the PR has already moved to another head SHA.
5. GitHub Models is always attempted with the job-scoped `GITHUB_TOKEN` and `models: read`. OpenAI Codex and Claude are optional additional independent providers when `OPENAI_API_KEY` or `ANTHROPIC_API_KEY` are configured.
6. The aggregator updates one sticky PR comment. If `WORKSLIP_REVIEW_PAT` is configured, the configured `rasm105k` identity is verified and used; otherwise the trusted aggregate job posts as `github-actions[bot]`.
7. The aggregator publishes an `AI PR Review` commit status on the exact reviewed PR head SHA.
8. A blocking AI status requires at least two independent available providers to report a matching `high` or `critical` finding with confidence >= 0.80. A single-provider review remains advisory.
9. No AI path approves or merges a pull request.

## Optional repository secrets

The baseline path requires no repository secret beyond GitHub's automatically issued `GITHUB_TOKEN`.

Optional integrations:

- `OPENAI_API_KEY` — enables OpenAI Codex as an additional independent reviewer.
- `ANTHROPIC_API_KEY` — enables Claude as an additional independent reviewer.
- `WORKSLIP_REVIEW_PAT` — optional fine-grained personal token for posting the sticky review comment as `rasm105k` instead of `github-actions[bot]`. Keep it restricted to this repository and the minimum PR/comment permission required by GitHub.

Missing optional credentials must never make an otherwise valid review fail. A red/error review status is reserved for a real review failure: no provider available, review publishing failure, or a consensus blocker.

## Security boundary

`workflow_run` is used deliberately. Review jobs run from the trusted default-branch workflow definition after normal PR CI has completed. Contributor-controlled PR code is never checked out or executed in those jobs.

The untrusted PR title/body/diff can contain prompt-injection text. The review prompt explicitly treats all PR content as data. Provider jobs have read-only repository/PR access; the GitHub Models job additionally has only `models: read`. Model actions are pinned to reviewed commit SHAs. Codex uses a read-only permission profile with sudo dropped; Claude receives only read/search tools plus the workflow's explicit read-only `GITHUB_TOKEN`.

Only the trusted aggregate job receives `pull-requests: write` and `statuses: write`. It never receives `contents: write` or `id-token: write`.

Diffs larger than 420 KB are truncated. A truncated review can report advisory findings but can never create a consensus blocker.

## Cost and throughput

AI review starts only after CI is green. Automatic review is restricted to invited/trusted contributor associations, and concurrency is per PR so a newer review cancels an older in-progress review.

The sticky comment is updated on each reviewed SHA rather than adding a new conversation comment for every push.

## Rollout

Keep `AI PR Review` advisory until one real GitHub Models run has completed end to end from the trusted default-branch workflow. Ruleset enforcement is a separate decision and must not be combined with provider rollout.
