# AI review rollout gate

The baseline review path uses GitHub Models with the workflow `GITHUB_TOKEN`; separate OpenAI/Anthropic keys and `WORKSLIP_REVIEW_PAT` are optional.

Before treating the workflow as an active review signal, observe one real pull request end to end after the no-key workflow reaches the trusted default branch.

Required evidence:

- normal `CI` succeeds on the reviewed PR SHA;
- the GitHub Models job runs from trusted `main`, has `models: read`, and does not checkout the PR head;
- at least one model review is returned and aggregated;
- the sticky review comment is created or updated using `github-actions[bot]` when `WORKSLIP_REVIEW_PAT` is absent;
- optional OpenAI/Claude provider failure degrades rather than blocking when another provider succeeds;
- zero available providers produces an error status instead of a false green;
- a synthetic matching high-confidence high-severity pair from two independent providers makes only the AI review check red and does not merge or mutate code;
- no API key, token, e-mail address or other intentionally seeded secret marker appears in Actions output or the PR review comment.

Keep the workflow advisory until this evidence exists. Ruleset enforcement is a separate decision.
