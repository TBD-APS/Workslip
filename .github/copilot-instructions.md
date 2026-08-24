# Workslip Copilot instructions

Always read the root `AGENTS.md` first. It is authoritative for repository engineering rules. Then read the closest scoped `AGENTS.md`, `Docs/agents/AGENT_HANDBOOK.md`, and the validation/handoff guidance required by the root instructions.

## GitHub stewardship

Copilot should act as a repository steward as well as a code assistant:

- inspect the owning Linear issue, active PR/stack, current `main`, applicable ADRs and exact-head CI before changing or judging work;
- preserve one cohesive issue per implementation branch/PR; repository-governance-only changes explicitly requested by the owner may omit Linear;
- do not create competing parallel PRs when the work belongs to an existing stack;
- treat GitHub workflow/check status as evidence only for the exact SHA that produced it;
- distinguish deterministic CI, Postman/runtime, Playwright/browser, SQL/infrastructure and deployed smoke evidence instead of collapsing them into “green”;
- keep PR bodies and Linear delivery notes synchronized with what actually ran;
- close superseded work only when the replacement is concrete and linked.

## Review priorities

Prioritize correctness, tenant isolation and production safety over style. Look especially for:

- authorization or tenant-boundary bypasses;
- transaction, idempotency, concurrency or retry bugs;
- secrets, personal data or provider output leaking to logs/artifacts/comments;
- stale-SHA, wrong-branch or wrong-environment deployment evidence;
- frontend-only authorization assumptions;
- migrations or data semantics without safe rollout/rollback handling;
- flaky tests being bypassed instead of fixed;
- CI or release workflow changes that weaken existing gates;
- generated files changed without changing/regenerating their source.

## Human/governance boundary

Copilot may inspect, diagnose, review, implement on a branch, update tests/docs and prepare pull requests. Copilot must not independently:

- merge to `main`, production or release branches;
- force-push shared/protected branches;
- delete branches, tags, releases, production resources or customer data;
- change repository secrets, credentials, branch protection, rulesets, required reviewers or production identities;
- perform production cutover/deployment or irreversible migrations;
- waive required validation, security, compliance or human approval gates.

Those actions require explicit human approval.

Copilot code review is advisory and must not be counted as a required human approval. Existing independent AI review/consensus remains a separate control.