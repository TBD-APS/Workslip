---
title: 'Use existing non-production identities for real OTC Playwright auth'
type: 'refactor'
created: '2026-08-08'
status: 'done'
baseline_commit: 'f17eb583d469782b5f7fdc88d6e7ff0c2b87b738'
context:
  - 'AGENTS.md'
  - 'Docs/agents/VALIDATION.md'
  - 'Docs/compliance/GDPR_AI_ACT_BASELINE.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** PR #403 currently depends on an Exchange shared mailbox, Microsoft Graph and GitHub OIDC to read one-time codes. The available interim identities are four existing, role-stable non-production users whose inboxes cannot currently be read safely by CI.

**Approach:** Configure the four existing identities through the retained `WORKSLIP_SYNTHETIC_*_EMAIL` variables, keep authentication on `/api/auth/send-code` and `/api/auth/verify-code/{code}`, and remove the shared-mailbox, Graph/OIDC and bootstrap design. Authenticated automation must fail closed before sending mail when no inbox reader exists; a deliberately enabled local interactive run may accept the code through the visible browser without exposing it to the Node process or artifacts.

## Boundaries & Constraints

**Always:** Keep PR #403 based directly on PR #400; preserve one stable Workslip role per configured identity; verify `/api/auth/me` after OTC login; retain the four role-specific email environment-variable names; keep personal addresses out of repository files, reports and logs; make the missing automated inbox capability explicit in workflow output, maintained docs, PR and Linear; keep the PR draft until deployed OTC Playwright succeeds.

**Ask First:** Adding any mailbox credential, provider API, OAuth grant, browser session, CI secret, new external processor, or role mutation; changing the PR base; treating a production/customer identity as test data.

**Never:** Call or restore `/api/dev/token`; mint or embed static application tokens; fall back from failed OTC to another auth path; retain Graph, Entra workload-identity, shared-mailbox, `id-token: write`, mailbox bootstrap, or long-lived Superadmin-token dependencies; print OTC values or full configured email addresses.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|---------------------------|----------------|
| Public smoke | No inbox access | Public scenario runs without authenticated identity setup | No auth mail is sent |
| CI authenticated scenario | Role emails configured; no approved inbox reader | Scenario stops before `/api/auth/send-code` | Clear fail-closed prerequisite message; no token fallback |
| Local interactive OTC | Explicit opt-in, TTY and headed browser | Operator enters the delivered code in the real login UI; normal verify endpoint returns the app JWT | Invalid/missing code fails normally through OTC |
| Misconfigured role | Missing email variable or returned role differs | Authentication is rejected | Name the missing variable/expected role without logging the address |

</frozen-after-approval>

## Code Map

- `.github/workflows/playwright-prod-smoke.yml` -- release-test permissions, source checks and role-email variables.
- `src/FE/scripts/playwright-prod-smoke.mjs` -- Playwright session/login orchestration and fail-closed boundary.
- `src/FE/scripts/playwright-synthetic-auth.mjs` -- current Graph/shared-mailbox reader to replace with provider-neutral OTC configuration.
- `src/FE/scripts/bootstrap-synthetic-test-identities.mjs` -- obsolete bootstrap for identities that now already exist.
- `Docs/operations/synthetic-test-identities.md` -- maintained operational truth for the temporary identity model.

## Tasks & Acceptance

**Execution:**

- [x] `.github/workflows/playwright-prod-smoke.yml` -- remove OIDC permission and Graph/mailbox variables; retain only role-email variables and validate the replacement helper/tests.
- [x] `src/FE/scripts/playwright-prod-smoke.mjs` and auth helper/tests -- preserve real OTC navigation while enforcing non-interactive fail-closed behavior before mail is sent; support explicit safe local interaction.
- [x] `src/FE/scripts/bootstrap-synthetic-test-identities.mjs` -- remove the now-invalid privileged bootstrap path.
- [x] `Docs/operations/synthetic-test-identities.md` -- document interim accounts, configuration ownership, test limitations, data handling and exact remaining validation.
- [x] PR #403 and Linear WOR-357 handoff -- prepare verified implementation/validation facts for the required post-review remote update.

**Acceptance Criteria:**

- Given the PR diff is searched, when WOR-357-specific files are inspected, then no Graph/OIDC/shared-mailbox/bootstrap dependency or `id-token: write` remains.
- Given an authenticated CI scenario has no inbox reader, when it starts, then it fails before sending an OTC and does not call `/api/dev/token` or use a static application token.
- Given a local operator explicitly enables interactive OTC, when the code arrives, then it is entered only in the visible Workslip login UI and verified through the normal endpoint.
- Given repository role-email variables are updated, when the harness runs, then no source change is required to substitute the identities.

## Spec Change Log

## Design Notes

Failing before `send-code` avoids spamming mailboxes and makes the unavailable capability deterministic. Interactive validation remains opt-in and headed so the OTC stays inside the browser field; CI never silently waits for human input.

## Verification

**Commands:**

- `node --check` for all affected Playwright modules and `node --test` for the OTC helper -- expected: syntax and fail-closed tests pass.
- `npm run lint`, `npm test -- --run`, and `npm run build` in `src/FE` -- expected: affected validation passes or existing unrelated baseline failures are identified precisely.
- `rg` across WOR-357 workflow/docs/code -- expected: no obsolete auth dependencies and no `/api/dev/token` use in the critical harness.
- GitHub/Linear reads after update -- expected: #403 remains stacked on #400, is draft when deployed OTC is unproved, and WOR-357 reflects the same boundary.

**Manual checks:**

- If mailbox access is available to the operator, run `auth-session` against the deployed target with explicit interactive OTC enabled and record browser/viewport, endpoints and outcome without storing the code.

## Suggested Review Order

**Authentication boundary**

- Start with the scenario preflight and real interactive OTC verification path.
  [`playwright-prod-smoke.mjs:36`](../../../src/FE/scripts/playwright-prod-smoke.mjs#L36)

- Role variables and explicit headed/TTY gating implement fail-closed behavior.
  [`playwright-synthetic-auth.mjs:3`](../../../src/FE/scripts/playwright-synthetic-auth.mjs#L3)

- OTC path redaction keeps codes out of reports and failure evidence.
  [`playwright-critical-contract.mjs:186`](../../../src/FE/scripts/playwright-critical-contract.mjs#L186)

**Delivery and operations**

- Workflow permissions retain only repository read access and role variables.
  [`playwright-prod-smoke.yml:35`](../../../.github/workflows/playwright-prod-smoke.yml#L35)

- Operational guidance records temporary identities and the unvalidated deployed boundary.
  [`synthetic-test-identities.md:9`](../../operations/synthetic-test-identities.md#L9)

**Regression evidence**

- Source tests cover public isolation, fail-closed preflight, TTY gating, and redaction.
  [`playwright-synthetic-auth.test.mjs:15`](../../../src/FE/scripts/playwright-synthetic-auth.test.mjs#L15)
