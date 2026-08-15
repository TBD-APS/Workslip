# CI quality gates

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** `.github/workflows/`, repository rulesets, Vercel project configuration and current successful runs  
**Review cadence:** When workflows, deployment targets or required checks change

## Principle

Workslip uses one normal delivery path:

`rbj--<issue>-...` branch → pull request → `CI Gate` → explicit manual merge → `main` → exact-SHA post-merge `CI Gate` → production.

`main` is the production code boundary. A separate `release/**` candidate branch is not part of the active release process.

A production mutation is **fail-closed**. Frontend deployment, backend deployment, production migrations and production infrastructure reconciliation must prove that the exact candidate SHA:

1. is the current `main` SHA;
2. has a completed `CI` push run for that exact SHA; and
3. has exactly one completed `CI Gate` job with conclusion `success`.

`failure`, `cancelled`, `timed_out`, `action_required`, `neutral`, `skipped`, a missing gate, a duplicate gate, a stale SHA or an unresolved CI run is not deployable. An older green ancestor is not sufficient.

The contract has two platform adapters because Vercel may isolate a configured Root Directory from repository-level files:

- `tools/release/verify-production-eligibility.mjs` — GitHub Actions/manual production operations;
- `src/FE/scripts/vercel-production-eligibility.mjs` — Vercel production build, physically inside the frontend Root Directory.

Both implement the same exact-SHA/green-gate invariants and both are covered by `Production delivery · Self-test`. Do not add a third interpretation of “green”.

## Pull request CI

`.github/workflows/frontend-validation.yml` is the unified `CI` workflow. It runs for every pull request to `main`, so every change gets the same merge signal rather than a collection of path-specific checks.

The merge signal is the `CI Gate` job. It succeeds only when these jobs succeed:

- `Backend` — full Release restore, build and backend test suite.
- `Frontend + API contract` — no-new-errors ESLint ratchet, branch-matched OpenAPI/Orval generation, generated-client parity, Vitest and production frontend build.
- `Contracts + docs` — production release-policy checks, release-runner and synthetic-auth fail-closed tests, Playwright source checks, Postman JSON validation and `python tools/docs/check_docs.py`.

The full backend suite is blocking. Do not replace it with a filtered allowlist, skips or `continue-on-error` to make CI green; repair failing regression tests or production code instead.

The frontend carries inherited ESLint debt. CI compares pull-request findings with the exact base revision and blocks new severity-2 errors without treating inherited findings as permission to grow the baseline.

The branch-matched frontend client is generated from the backend in the same revision. After generation, CI requires `src/FE/src/api/generated` to be clean. This matters because Vercel production intentionally does not regenerate against a remote dev/prod OpenAPI endpoint; the client committed in the release SHA must already be the client CI proved against that backend revision.

## Code scanning

GitHub CodeQL **Default setup** is the repository's code-scanning owner.

Do not add `github/codeql-action` jobs to the normal CI while Default setup is enabled. GitHub rejects advanced-configuration uploads when Default setup owns the repository, creating duplicate work and permanently red checks rather than additional protection.

Whether code-scanning findings are merge-blocking is repository security/ruleset state and must be verified in GitHub settings. CI workflow YAML must not duplicate that external control.

## Main verification

The same `CI` workflow runs after a merge to `main`.

Core backend, frontend/API-contract and contract/documentation checks run again against the exact production revision. Code scanning remains owned by GitHub Default setup rather than being duplicated in the CI workflow.

Both application surfaces depend on that exact post-merge evidence:

- Vercel may receive the Git push immediately, but its configured `buildCommand` runs the root-local Vercel adapter, waits for the exact SHA to have a successful `CI Gate`, and verifies the SHA is still current `main` before the frontend build proceeds.
- Azure backend delivery is triggered by the completed `CI` run and validates the triggering run, exact SHA and `CI Gate`; it repeats the current-main check immediately before migrations/deployment so a release that becomes stale during artifact build cannot mutate production.

This prevents frontend/backend drift caused by one platform releasing while the other revision is red, cancelled, stale or still validating.

## Production deployment

### Frontend · Vercel production

`src/FE/vercel.json` permits Git deployment only from `main`.

The production build command first runs:

`node scripts/vercel-production-eligibility.mjs`

The adapter uses Vercel's exact Git commit metadata, requires the `main` branch, waits for the exact post-merge `CI Gate`, and performs a second `main` read after validating the CI jobs so a SHA that becomes stale during verification is rejected. Only then does Vercel run the deterministic frontend build.

The adapter intentionally lives under `src/FE`. Vercel documents that a configured Root Directory may prevent builds from accessing source outside that directory unless a separate project setting enables it. Production safety therefore does not depend on that dashboard option.

The production build does **not** call `generate:api:dev` or fetch OpenAPI from a remote development API. The generated API client is committed and its parity with the same-revision backend contract is a blocking CI check.

A Vercel deployment record can therefore exist while CI is pending, but it is not a successful production release until both the exact-SHA gate and the Vercel build succeed.

### Backend · Azure production

`.github/workflows/backend-production-deploy.yml` is named **Backend · Production deploy** and listens for completed `CI` runs on `main`.

The workflow:

1. validates the triggering workflow is the canonical `CI` run for the exact current `main` SHA and that its `CI Gate` succeeded;
2. builds and packages the API from that exact SHA;
3. re-runs the production eligibility check before any Azure mutation;
4. keeps Azure OIDC permission scoped to the protected deployment job;
5. resolves the dedicated migration identity and applies reviewed migrations;
6. verifies required diagnostics configuration;
7. applies the production release-testing policy;
8. deploys the exact-SHA artifact with bounded retries and captures Azure deployment diagnostics on failure; and
9. requires the API `/health` endpoint to recover before reporting the release successful.

The old ancestor check is intentionally not used. A previously green SHA that is merely contained in a newer `main` is stale and cannot deploy.

### Database · Production migrations

`.github/workflows/database-production-migrations.yml` is the explicit manual migration workflow. The `MIGRATE` confirmation is operator intent, not a validation bypass. Before Azure login or mutation it requires the selected ref to be `main` and the selected exact SHA to pass the same production eligibility gate. Node 24 is set up explicitly before the gate rather than relying on runner-image defaults.

### Infrastructure · Production reconcile

`.github/workflows/infrastructure-production-reconcile.yml` remains the separate privileged infrastructure path. Before OIDC login or infrastructure mutation it requires `main` and a successful exact-SHA `CI Gate`. It preserves the dedicated infrastructure identity and post-reconcile API health verification and sets up Node 24 explicitly for the gate.

### Production · Readiness smoke

`.github/workflows/production-readiness-smoke.yml` is the maintained public production smoke. It is read-only, but still requires an exact green `main` SHA so evidence cannot accidentally be attached to an unvalidated revision.

Authenticated/destructive Playwright evidence remains blocked until the isolated staging target and approved test authentication are completed; public smoke never substitutes for pre-merge CI.

## Deployment naming and environment ownership

Canonical workflow/job naming is `<Surface> · Production <action>` so GitHub Actions shows both the system and the target directly. Examples are `Backend · Production deploy`, `Database · Production migrations`, `Infrastructure · Production reconcile` and `Production · Readiness smoke`.

Stable cloud resource names are not renamed merely for aesthetics. The active environment inventory is:

- GitHub `prod` — Workslip's protected Azure application/infrastructure environment. It carries the existing Azure environment configuration and permits deployment from `main`; keep this stable until a separately verified secret/variable migration can be performed.
- GitHub `Production` and `Preview` — Vercel Git integration environments with active Vercel-created deployment records; they are not duplicates of the Azure `prod` environment.
- GitHub `github-pages` — independent GitHub Pages deployment environment.
- GitHub `copilot` — GitHub/Copilot integration environment, not an application production path.
- Vercel project `workslip-v2-0` — frontend hosting target; only `main` Git deployments are enabled by repository configuration.
- Azure `api-mrsoftware-prod` in `rg-mrsoftware-prod` — backend production target.

Do not delete or rename an environment from its name alone. Verify usage, deployment records, secrets/variables and external integration ownership first.

## Repository protection

The repository `main` ruleset is defense in depth and must enforce:

- pull request required;
- required status checks `CI Gate` and `Feature change guard`;
- no bypass actors;
- direct pushes blocked by the pull-request rule;
- non-fast-forward/force pushes blocked;
- squash-only merge; and
- merge remains an explicit human action.

Current repository inspection for WOR-468 found the active `Prod Ruleset` requires a pull request, non-fast-forward protection and CodeQL, but does **not** yet contain `CI Gate` as a required status check and still has configured bypass actors. That settings gap must be corrected in GitHub repository administration; it is not represented as fixed merely by changing workflow YAML.

`tools/release/configure-github-branch-rules.ps1` is the authoritative reconciliation command. Its apply payload contains no bypass actors, and its read-back verification now fails if GitHub reports bypass actors, wrong refs/rule types, wrong merge methods, wrong review count, wrong required checks or a non-strict status-check policy. `-VerifyOnly` is the acceptance evidence after an administrator applies the rules.

Production delivery no longer trusts that ruleset as its only red-deploy defense: every maintained production mutation independently fails closed on the exact post-merge `CI Gate`. The ruleset remains necessary to prevent unvalidated code or modified gate logic from being placed on `main` in the first place.

## Production delivery self-test

`.github/workflows/production-delivery-selftest.yml` protects the delivery implementation itself. It verifies:

- both Actions and Vercel adapters reject red/cancelled/stale/missing/duplicate gates;
- Vercel production uses the Root-Directory-local exact-SHA gate and has no `generate:api:dev` or parent-directory dependency;
- all privileged production workflows use the shared `workslip-production` lock and `prod` environment;
- backend deployment revalidates before mutation and cannot fall back to ancestor semantics;
- the repository-protection source requires `CI Gate`, `Feature change guard`, no bypass actors and strict status checks; and
- retired legacy workflow entrypoints do not reappear.

## Releases and tags

GitHub tags/releases are optional release-history markers for meaningful product versions. They do not control production deployment and must not recreate a second release pipeline.

## Security

Use GitHub OIDC for Azure deployment. Grant `id-token: write` only to jobs that need Azure tokens. Do not introduce publish profiles, long-lived Azure credentials or privileged repository-writing automation without a concrete requirement and reviewed least-privilege design.

The durable decision behind this model is recorded in [`../architecture/adr/0005-main-as-production-boundary.md`](../architecture/adr/0005-main-as-production-boundary.md).
