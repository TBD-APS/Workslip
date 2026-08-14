# Workslip AI pull-request review

**Status:** Draft until the three repository secrets are configured and one real PR review is observed end to end.

This automation adds a second review signal after Workslip's normal `CI` succeeds. It does not replace CI, CodeQL, human review, browser/runtime evidence or the repository owner's explicit merge decision.

## Review model

1. The existing `CI` workflow validates the PR first.
2. `AI PR Review` runs only after a successful PR CI run, or by explicit manual dispatch.
3. The workflow checks out trusted `main` only. It never checks out or executes the pull-request head in a secret-bearing job.
4. PR metadata and the unified diff are fetched through the GitHub API, size-bounded, redacted and marked as untrusted data.
5. OpenAI Codex and Claude review the same context independently with read-only tools.
6. The aggregator updates one sticky PR comment through the configured `rasm105k` token.
7. A red AI signal is emitted only when both available models independently report a matching `high` or `critical` finding with confidence >= 0.80. One-model findings remain advisory.
8. No AI path approves or merges a pull request.

## Required repository secrets

Configure these in GitHub repository Actions secrets:

- `OPENAI_API_KEY` — API key dedicated to CI review usage.
- `ANTHROPIC_API_KEY` — API key dedicated to CI review usage.
- `WORKSLIP_REVIEW_PAT` — a fine-grained GitHub personal access token owned by `rasm105k`, restricted to **only** `rasm105k/Workslip-v2.0`, with Metadata read and Issues read/write. Do not grant Contents, Administration, Actions, Secrets or repository-management write access.

The workflow calls `/user` before posting and fails closed if `WORKSLIP_REVIEW_PAT` does not belong to `rasm105k`. `GITHUB_TOKEN` is intentionally not used for the final comment because that would publish as `github-actions[bot]` rather than the configured personal account.

If GitHub changes the fine-grained permission required by the PR conversation-comments endpoint, add only the smallest documented permission needed for that endpoint; do not broaden the token pre-emptively.

## Security boundary

`workflow_run` is used deliberately. The secret-bearing review jobs run from the trusted default-branch workflow definition after normal PR CI has completed. Contributor-controlled PR code is never checked out or executed in those jobs.

The untrusted PR title/body/diff can contain prompt-injection text. The review prompt explicitly treats all PR content as data, and model jobs receive no write-capable GitHub token. Model actions are pinned to reviewed commit SHAs. Codex uses a read-only permission profile with sudo dropped; Claude receives only read/search tools.

The personal GitHub token is isolated to the aggregator job after model execution, so PR content is never supplied to a model in a process that also holds the personal posting credential.

Diffs larger than 420 KB are truncated. A truncated review can report advisory findings but can never create a consensus blocker.

## Cost and throughput

AI review starts only after CI is green, so model spend is not consumed by PRs that already fail deterministic validation. Concurrency is per PR and a newer review cancels an older in-progress review.

The sticky comment is updated on each reviewed SHA rather than adding a new conversation comment for every push.

## Rollout

Keep `AI PR Review` advisory initially. After enough real PRs establish signal quality and false-positive rate, separately decide whether the repository ruleset should require the AI check. Do not combine that enforcement change with this workflow rollout.

The current main ruleset must also be reviewed separately for human approvals, required conversation resolution and `CI Gate` enforcement before the team scales further.
