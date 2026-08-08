---
title: 'Gate backend production deploy behind validated release candidate'
type: 'feature'
created: '2026-08-08'
status: 'done'
baseline_commit: 'bcab2e10ee198abbbf2e4b5300ac265eb0782cf3'
context:
  - '{project-root}/AGENTS.md'
  - '{project-root}/Docs/agents/VALIDATION.md'
  - '{project-root}/Docs/AGENTS.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The backend currently deploys automatically when relevant files reach `main`, so production can receive a commit before that exact commit has passed the `release/**` validation gate. The workflow also permits a manual run without proving that the selected code is a validated release candidate.

**Approach:** Run the manual deployment orchestrator only from the trusted default branch and accept an explicit `release/**` ref plus exact candidate SHA as data. Deploy only when that SHA is the release-branch head, is already contained in `main`, uses the same release-validation workflow definition as trusted `main`, and has a successful `Release validation` push run for the same ref/SHA; restrict the existing `prod` environment to `main` so historical or candidate-controlled workflow revisions cannot deploy.

## Boundaries & Constraints

**Always:** Use a new `workflow_dispatch` identity on `main` as the only backend production trigger and keep the legacy automatic workflow disabled; require an explicit `release/**` ref and full candidate SHA; verify the trusted deployment-workflow identity/ref, release branch head, candidate reachability from `main`, release-validation workflow provenance, and successful same-ref/SHA validation before build or environment access; check out the exact candidate SHA in every source-dependent job; restrict `prod` deployments to `main`; expose candidate ref, SHA, validation workflow digest/run, artifact digest, orchestration SHA, and successful health result as evidence; grant OIDC only to the deploy job; keep Azure target, retries, release-testing policy, diagnostics prerequisite, and health behavior unchanged.

**Ask First:** Changing the Azure target, OIDC federation, `prod` reviewers or approval behavior, release-validation coverage, deployment retry limits, health-check semantics, or allowing candidates not already merged to `main`.

**Never:** Reintroduce automatic deployment from `main`; execute the deployment orchestrator from a candidate or historical release ref; accept tags, non-release inputs, partial SHAs, release-only commits, mismatched branch heads, altered/older validation definitions, or merely queued/failed validation runs; deploy unvalidated code; add Vercel/frontend production work or any WOR-369 scope; weaken current safety checks to simplify the gate.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Valid candidate | Workflow is dispatched on `main`; input ref is `release/2026-08-08-rc1`; input SHA is its head, is in `main`, matches trusted validation definition, and has a successful validation push run | Gate records provenance, build uses the exact SHA, and `prod` deploy may proceed through environment controls | N/A |
| Untrusted orchestrator ref | Deployment workflow is dispatched from a release branch, tag, or non-default branch | No build or production job runs; `prod` also rejects the ref | Gate emits an actionable error and fails |
| Release-only or moved candidate | SHA is not current release-ref head or is not reachable from current `main` | Candidate is rejected even if another validation run exists | Gate reports the failed provenance condition and exits |
| Untrusted validation | Candidate's `release-validation.yml` blob differs from current trusted `main`, or same-ref/SHA successful push run is absent | Candidate is not built or deployed | Gate records expected/actual provenance and fails |

</frozen-after-approval>

## Code Map

- `.github/workflows/backend-production-release.yml` -- new trusted-`main` backend release orchestrator; owns the candidate gate, OIDC, `prod`, retries, and health verification.
- `.github/workflows/main_api-mrsoftware-prod.yml` -- legacy automatic workflow; removed from the repository and its GitHub workflow ID remains disabled.
- `.github/workflows/release-validation.yml` -- authoritative `release/**` full-code validation workflow and final `Release gate`.
- `Docs/operations/ci-quality-gates.md` -- maintained CI/release/deployment boundary and operator guidance.
- `Docs/operations/playwright-critical-flows.md` -- references backend deployment's release-policy resolution; verify wording remains accurate.
- GitHub Environment `prod` -- external deployment boundary; custom branch policy must allow only `main`.

## Tasks & Acceptance

**Execution:**
- [x] `.github/workflows/backend-production-release.yml` -- replace the legacy automatic workflow with a new manual workflow ID requiring explicit release ref plus exact candidate SHA and trusted `main` execution.
- [x] `.github/workflows/backend-production-release.yml` -- add a fail-closed pre-build provenance gate for trusted orchestrator ref, release head, `main` ancestry, validation workflow blob, and successful same-ref/SHA validation run.
- [x] `.github/workflows/backend-production-release.yml` -- build the immutable validated SHA, keep deploy-only OIDC and existing safety steps, and publish the complete successful-deployment evidence chain.
- [x] Legacy GitHub workflow ID `321365100` -- verify no active/queued/waiting runs and disable it so pre-gate runs cannot remain the normal deploy path.
- [x] GitHub Environment `prod` -- enable custom deployment branch policy with `main` as the sole allowed branch without changing secrets or reviewer behavior.
- [x] `Docs/operations/ci-quality-gates.md` -- document the trusted-`main` operator flow, environment policy, evidence, rejection behavior, and WOR-369/Vercel separation.

**Acceptance Criteria:**
- Given a relevant commit merges to `main`, when no release branch is created and no manual production dispatch occurs, then backend production is unchanged.
- Given a validated `release/**` branch at exact SHA `S` already contained in `main`, when an operator dispatches the backend workflow from `main` with that ref and `S`, then the workflow can reach `prod` and records matching provenance and validation evidence.
- Given an untrusted orchestrator ref, release-only/moved SHA, changed validation definition, or absence of a successful same-ref/SHA validation push run, when dispatch occurs, then it fails before build, `prod`, or Azure authentication.
- Given the deploy path proceeds, when the workflow builds and deploys, then the artifact originates from exact candidate SHA `S`, only the deploy job receives OIDC, and successful health evidence is tied to the validation run and artifact digest.
- Given a pre-gate legacy workflow or a non-`main` candidate-controlled revision targets `prod`, when GitHub evaluates the normal release path, then the disabled legacy workflow identity and `main`-only environment policy prevent deployment.

## Spec Change Log

- 2026-08-08 review loop 1: adversarial, edge-case and acceptance reviews proved that dispatching from `release/**` lets the candidate control both the deployment gate and validation definition. Replaced the frozen intent with the human-approved trusted-`main` orchestrator, explicit release ref/SHA inputs, `main` ancestry and validation-blob provenance checks, plus a `prod` main-only branch policy. This avoids historical or candidate-modified workflows bypassing the gate. KEEP: manual-only production deployment, exact-SHA validation/evidence, fixed Azure target and `prod`, deploy-only OIDC, retries, diagnostics/release-testing safety, health checks, and strict WOR-369 exclusion.
- 2026-08-08 review loop 2: edge-case and acceptance reviews showed that a pre-gate `main` run could otherwise be rerun with its historical YAML while still matching the `prod` branch policy. Moved the gated deployment to a new workflow path/identity and disabled legacy workflow ID `321365100` after verifying no active or pending runs. This avoids leaving historical automatic runs as the normal deploy route. KEEP: trusted-current-`main` gate, `prod` main-only policy, pinned gate dependency, exact candidate/artifact evidence, and all existing deployment safety behavior.
- 2026-08-08 integration update: rebased onto merged PR #413, retained its release-candidate operating guidance, and replaced its first gate implementation because it reused the historical workflow identity and did not bind the accepted validation definition to trusted `main`. KEEP: manual operator inputs, candidate-in-`main` rule, exact-SHA build, deploy-only OIDC, production safeguards, and health verification.

## Design Notes

The workflow definition that authorizes production must not come from the candidate it authorizes. The operator therefore dispatches the default-branch workflow and supplies the release ref/SHA as data. Candidate ancestry proves the release branch contains an already-merged commit, and matching the candidate validation-workflow blob to current `main` prevents an older or weakened candidate-owned definition from satisfying the run query.

GitHub records the environment deployment against the trusted orchestration run's `main` SHA, not the deployed candidate SHA. The final job summary is therefore the release evidence: it explicitly relates orchestration SHA, release ref, candidate SHA, validation definition/run, built artifact digest, health URL, and successful deployment run. The `prod` custom branch policy is the external backstop that prevents historical branch versions of this workflow from reaching environment secrets or OIDC.

## Verification

**Commands:**
- `actionlint .github/workflows/backend-production-release.yml .github/workflows/release-validation.yml` -- expected: workflow syntax and expression validation pass when `actionlint` is available.
- `python tools/docs/check_docs.py` -- expected: maintained documentation checks pass.
- `git diff --check` -- expected: no whitespace errors.
- `gh api repos/rasm105k/Workslip-v2.0/environments/prod` plus its deployment-branch-policies endpoint -- expected: custom policies enabled and the only branch policy is `main`.

**Manual checks (if no CLI):**
- Review the workflow dependency graph and permissions to prove no source build, `prod` environment job, Azure login, or deployment step can run after a failed candidate gate.
- After merge, use a controlled release candidate to prove one rejected unvalidated/mismatched dispatch and one successful validated dispatch before relying on the workflow for production releases.

## Suggested Review Order

1. Review the trusted `main` trigger and fail-closed candidate gate in [backend-production-release.yml](../../../.github/workflows/backend-production-release.yml#L1), especially the orchestrator identity, release-head, `main` ancestry, validation-definition, and successful-run checks.
2. Review exact-SHA checkout, package digest creation, deploy-only OIDC, digest verification, bounded retries, and health evidence in [backend-production-release.yml](../../../.github/workflows/backend-production-release.yml#L224).
3. Review the operator procedure, external `prod` branch policy, legacy-workflow shutdown, evidence fields, and WOR-369 boundary in [ci-quality-gates.md](../../operations/ci-quality-gates.md#L37).
4. Confirm the repository deletion of `.github/workflows/main_api-mrsoftware-prod.yml` matches disabled legacy workflow ID `321365100` and that the GitHub `prod` environment still allows only `main`.

Stop review if the deployment workflow can run from anything except current `main`, if a candidate can alter its accepted validation definition, if a build or environment job can start after a failed gate, or if any frontend/Vercel/WOR-369 change appears in this PR.
