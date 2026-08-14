# AI review rollout gate

Before this workflow can be treated as active, configure the three repository secrets documented in `README.md` and observe one real pull request end to end.

Required evidence:

- normal `CI` succeeds on the reviewed PR SHA;
- both model jobs run from trusted `main` and do not checkout the PR head;
- the aggregate comment is posted as `rasm105k` and updated rather than duplicated after a new push;
- one-provider failure degrades to advisory rather than blocking;
- a synthetic matching high-confidence high-severity pair makes only the AI review check red and does not merge or mutate code;
- no API key, token, e-mail address or other intentionally seeded secret marker appears in Actions output or the PR review comment.

Keep the workflow advisory until this evidence exists. Ruleset enforcement is a separate decision.
