# ADR 0005: Use `main` as the production boundary

**Status:** Accepted  
**Date:** 2026-08-08  
**Last amended:** 2026-08-15  
**Decision owners:** Workslip maintainers

## Context

Workslip originally had two production boundaries.

The Vercel frontend treated a merge to `main` as a production deployment, while the backend required a second `release/**` branch, a separate release-validation workflow and an explicit branch/SHA handoff before Azure deployment.

That split added operational ceremony and duplicated validation without preventing frontend code from reaching production first. It also made the meaning of `main`, release branches and GitHub Actions difficult to explain and easy to operate inconsistently.

The first simplification made `main` the single application production boundary and made the backend wait for a successful post-merge `CI` run. A later delivery review exposed two remaining failure modes:

1. Vercel still attempted production builds immediately from `main`, independent of the post-merge CI result. This allowed frontend production attempts while the corresponding revision was red, cancelled or still validating.
2. The backend deploy accepted a successful older CI SHA as long as it remained an ancestor of current `main`. That preserved deploy progress but did not guarantee that frontend, backend and deployment evidence referred to the same current application revision.

WOR-468 also verified that the active GitHub `main` ruleset did not yet require `CI Gate`. Merge protection is therefore important defense in depth, but production safety cannot rely exclusively on repository settings being configured correctly.

Vercel adds one platform-specific constraint: a project configured with `src/FE` as its Root Directory may prevent build commands from accessing repository files outside that directory unless a separate Vercel setting is enabled. The production gate must not depend on that mutable dashboard setting.

## Decision

`main` remains the single application production boundary.

Normal delivery is:

`rbj--<issue>-...` → pull request → `CI Gate` → explicit manual merge → `main` → exact-SHA post-merge `CI Gate` → production.

A production release or production mutation must prove that the exact candidate SHA:

- is the current `main` SHA;
- has a completed canonical `CI` push run for that exact SHA; and
- has exactly one completed `CI Gate` job with conclusion `success`.

All other states fail closed. This includes failed, cancelled, timed-out, skipped, neutral or missing checks; duplicate gates; a stale older SHA; and an in-progress run that does not become successful within the bounded wait used by the caller.

The contract has two purpose-specific adapters rather than depending on one file crossing hosting boundaries:

- `tools/release/verify-production-eligibility.mjs` for GitHub Actions and privileged production operations;
- `src/FE/scripts/vercel-production-eligibility.mjs` for Vercel, so the production build remains self-contained inside the configured frontend Root Directory.

Both adapters implement the same exact-SHA/green-gate invariants and are regression-tested by `Production delivery · Self-test`. A third deploy-eligibility interpretation must not be introduced.

### Pull-request validation

The pull-request `CI Gate` owns deterministic repository validation before merge:

- full backend Release build and test suite;
- frontend no-new-lint regression checking;
- branch-matched OpenAPI/Orval generation;
- generated API-client parity with the backend contract;
- frontend tests and production build; and
- repository contract/documentation checks.

Code scanning has one owner: GitHub CodeQL Default setup. The normal CI workflow must not add an advanced CodeQL configuration while Default setup is enabled. Whether code-scanning results block merge is repository security/ruleset configuration, not a second CI implementation.

### Frontend production

Vercel Git integration remains the frontend hosting mechanism and receives `main` Git changes, but the production build itself is gated.

`src/FE/vercel.json` overrides the production build command so it first runs the root-local Vercel adapter and waits for the exact-SHA post-merge `CI Gate`. The adapter reads current `main` before evaluating CI and again after the successful gate evidence, so a SHA that becomes stale during verification is rejected. Only then may the frontend build proceed.

Production Vercel builds do not regenerate the API client against a remote development or production API. CI generates the client from the backend contract in the same revision and requires the generated files already committed in that SHA to match. Vercel then builds those validated sources deterministically.

### Backend production

Backend production is triggered by a completed `CI` workflow run on `main`.

The backend workflow verifies that exact triggering CI run and its `CI Gate`, builds an artifact from exactly that SHA, then **re-validates current-main eligibility immediately before migrations or application deployment**. If `main` advanced during artifact construction, the older release is stale and cannot mutate production.

The previous `git merge-base --is-ancestor` acceptance is intentionally retired. Being a green ancestor is not equivalent to being the current validated application revision.

Azure deployment continues to use the protected `prod` environment, GitHub OIDC, a dedicated migration identity, bounded retries, deployment failure diagnostics and post-deploy health verification.

### Privileged manual production operations

A manual dispatch is operator intent, not a CI bypass.

Production database migrations and production infrastructure reconciliation may only be dispatched from `main` and must pass the exact-SHA Actions production eligibility adapter **before** acquiring Azure credentials or mutating resources. Both workflows explicitly set up Node 24 for the gate instead of relying on runner-image defaults. The production public readiness smoke is read-only but also requires exact green `main` so its evidence is attached to a validated revision.

### Concurrency

Pull-request CI remains disposable and may cancel when the same PR receives a newer commit.

An already-running `main` CI run is not cancelled by a later merge, preserving validation progress. Production mutations share one `workslip-production` concurrency group and do not cancel an in-progress production mutation.

Because mutation eligibility is revalidated before privileged changes, a later `main` SHA prevents an older queued/built release from becoming a new production mutation even though its CI was once green.

### Repository protection

The `main` ruleset must require a pull request, `CI Gate` and `Feature change guard`, no bypass actors, squash-only merge, and no deletion or non-fast-forward update. Merge remains an explicit human action.

`tools/release/configure-github-branch-rules.ps1` owns the intended ruleset payload and external read-back verification. Verification must fail when GitHub reports bypass actors, wrong ref targets/rule types, wrong merge methods/review count, wrong required checks or a non-strict status-check policy. Repository code is not evidence that the external ruleset is active; an administrator must apply it and `-VerifyOnly` must succeed against GitHub state.

This repository setting is a separate enforcement layer from deployment eligibility. A ruleset misconfiguration must not turn red or stale code into a deployable revision; conversely, deployment gating does not make a weak merge ruleset acceptable because a bypass could alter the gate implementation on `main` itself.

A separate `release/**` candidate branch is not used for normal production delivery.

Production infrastructure reconciliation and risk-based deployed Playwright scenarios remain separate operational workflows because they have different privileges and failure semantics from normal application delivery.

GitHub tags/releases may mark meaningful product versions, but they do not form another deployment gate.

## Environment naming and ownership

Workflow and job names use a surface-first production convention such as:

- `Backend · Production deploy`
- `Database · Production migrations`
- `Infrastructure · Production reconcile`
- `Production · Readiness smoke`

Stable external environment/resource identifiers are not renamed solely for presentation. The GitHub `prod` environment remains the Azure protected environment while it owns existing production variables/secrets and a `main` deployment policy. Vercel-created GitHub `Production` and `Preview` environments are distinct integration-owned deployment records, not duplicates of Azure `prod`.

An environment may be renamed or removed only after its integrations, deployment history, variables/secrets and dependent workflows have been verified and migrated.

## Consequences

### Positive

- One branch has one meaning: merged `main` is the production code boundary.
- Manual merge remains the explicit release decision.
- Both frontend and backend require the same exact post-merge green SHA before successful production delivery.
- Red, cancelled, incomplete and stale revisions fail closed independently of branch-protection configuration.
- Vercel production no longer depends on a remote development OpenAPI endpoint or access to files outside its configured Root Directory.
- Frontend/backend contract generation is proved against the same revision that Vercel builds.
- Manual migrations and infrastructure reconciliation cannot become hidden production bypasses.
- Ruleset verification now checks external bypass state instead of trusting only the desired payload.
- Deployment evidence can identify the exact SHA, CI run/gate and target.
- Issue-specific, release-branch-only and duplicate delivery automation can continue to be retired.

### Trade-offs

- A merge to `main` no longer means the frontend can finish production immediately; Vercel may wait for post-merge CI before the actual build proceeds.
- The Vercel platform adapter intentionally duplicates a small amount of exact-SHA evidence logic because Vercel Root Directory isolation is a real deployment boundary; self-tests must keep both adapters behaviorally aligned.
- If `main` advances while an older backend artifact is building, that artifact is intentionally abandoned rather than deployed. A later current SHA becomes the release candidate.
- Production delivery depends on GitHub API availability for exact-SHA evidence. Ambiguous or unavailable evidence fails closed rather than guessing.
- The `main` repository ruleset remains a critical administrative control even though deployment has an independent safety gate.
- Stable environment identifiers such as `prod` may remain less descriptive than workflow display names until their protected configuration can be migrated safely.
- Large or unusually risky changes can still require additional Playwright, infrastructure or operational evidence; the simplified branch model does not reduce risk-based testing requirements.
