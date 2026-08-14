# Workslip AI pull-request review

**Status:** Draft until the three repository secrets are configured and one real PR review is observed end to end.

This automation adds a second review signal after Workslip's normal `CI` succeeds. It does not replace CI, CodeQL, human review, browser/runtime evidence or the repository owner's explicit merge decision.

## Review model

1. The existing `CI` workflow validates the PR first.
2. `AI PR Review` runs automatically after successful PR CI only for `OWNER`, `MEMBER` or `COLLABORATOR` authors. External-contributor PRs require explicit `workflow_dispatch` so a public repository cannot be used to generate uncontrolled model spend.
3. The workflow checks out trusted `main` only. It never checks out or executes the pull-request head in a secret-bearing job.
4. PR metadata and the unified diff are fetched through the GitHub API, size-bounded, redacted and marked as untrusted data. A stale CI completion is skipped when the PR has already moved to another head SHA.
5. OpenAI Codex and Claude review the same context independently with read-only tools. A provider without a configured credential is treated as intentionally disabled, so Claude-only operation is a fully supported mode rather than a degraded one.
6. The aggregator updates one sticky PR comment through the configured `rasm105k` token.
7. The aggregator also publishes an `AI PR Review` commit status on the exact reviewed PR head SHA. This keeps later ruleset enforcement possible without changing the review architecture.
8. A failing AI status is emitted only when both available models independently report a matching `high` or `critical` finding with confidence >= 0.80. Single-provider findings are always advisory: with only Claude configured the review never blocks on its own, and degraded mode is reported only when a *configured* provider fails.
9. No AI path approves or merges a pull request.

## Required repository secrets

Configure these in GitHub repository Actions secrets:

- `ANTHROPIC_API_KEY` — API key dedicated to CI review usage. Required for Claude review.
- `WORKSLIP_REVIEW_PAT` — a fine-grained GitHub personal access token owned by `rasm105k`, restricted to **only** `rasm105k/Workslip-v2.0`, with Metadata read and Issues read/write. Do not grant Contents, Administration, Actions, Secrets or repository-management write access. Required for posting the review comment.
- `OPENAI_API_KEY` — optional. When present it adds the second independent reviewer and enables consensus blocking; when absent the workflow runs cleanly in Claude-only mode.

The workflow calls `/user` before posting and fails closed if `WORKSLIP_REVIEW_PAT` does not belong to `rasm105k`. `GITHUB_TOKEN` is intentionally not used for the final comment because that would publish as `github-actions[bot]` rather than the configured personal account.

If GitHub changes the fine-grained permission required by the PR conversation-comments endpoint, add only the smallest documented permission needed for that endpoint; do not broaden the token pre-emptively.

## Security boundary

`workflow_run` is used deliberately. The secret-bearing review jobs run from the trusted default-branch workflow definition after normal PR CI has completed. Contributor-controlled PR code is never checked out or executed in those jobs.

The untrusted PR title/body/diff can contain prompt-injection text. The review prompt explicitly treats all PR content as data, and model jobs receive no write-capable GitHub token. Model actions are pinned to reviewed commit SHAs. Codex uses a read-only permission profile with sudo dropped; Claude receives only read/search tools plus the workflow's explicit read-only `GITHUB_TOKEN`. The Claude job does not request `id-token: write`, preventing fallback to a broader GitHub App/OIDC credential path.

The personal GitHub token is isolated to the aggregator job after model execution, so PR content is never supplied to a model in a process that also holds the personal posting credential. The aggregator's built-in `GITHUB_TOKEN` can only read repository/PR data and write commit statuses; the personal PAT is used only for the conversation comment.

Diffs larger than 420 KB are truncated. A truncated review can report advisory findings but can never create a consensus blocker.

## Cost and throughput

AI review starts only after CI is green, so model spend is not consumed by PRs that already fail deterministic validation. Automatic paid review is also restricted to invited/trusted contributor associations. Concurrency is per PR and a newer review cancels an older in-progress review.

The sticky comment is updated on each reviewed SHA rather than adding a new conversation comment for every push.

## Rollout

Keep `AI PR Review` advisory initially. After enough real PRs establish signal quality and false-positive rate, separately decide whether the repository ruleset should require the `AI PR Review` commit status. Do not combine that enforcement change with this workflow rollout.

The current main ruleset must also be reviewed separately for human approvals, required conversation resolution and `CI Gate` enforcement before the team scales further.
