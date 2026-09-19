# CI quality gates

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** `.github/workflows/`, repository rulesets, `src/FE/Dockerfile`, `src/FE/nginx.conf` and current successful runs  
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

`tools/release/verify-production-eligibility.mjs` is the single adapter for that contract. Every maintained production workflow calls it before it acquires Azure credentials or mutates anything: `aca-live-deploy.yml`, `backend-production-deploy.yml`, `database-production-migrations.yml`, `infrastructure-production-reconcile.yml`, `appservice-slot-upgrade.yml`, `sql-live-seed-copy.yml` and `production-readiness-smoke.yml`. Do not add a second interpretation of “green”.

Frontend delivery no longer needs a second platform-specific adapter. The frontend is a container image built inside the same Azure workflow as the API, so it reads the same evidence from the same repository checkout.

## Pull request CI

`.github/workflows/frontend-validation.yml` is the unified `CI` workflow. It runs for every pull request to `main`, so every change gets the same merge signal rather than a collection of path-specific checks.

The merge signal is the `CI Gate` job. It succeeds only when these jobs succeed:

- `Backend` — full Release restore, build and backend test suite.
- `Frontend + API contract` — no-new-errors ESLint ratchet, branch-matched OpenAPI/Orval generation, generated-client parity, Vitest and production frontend build.
- `Contracts + docs` — production release-policy checks, release-runner and synthetic-auth fail-closed tests, Playwright source checks, Postman JSON validation and `python tools/docs/check_docs.py`.

The full backend suite is blocking. Do not replace it with a filtered allowlist, skips or `continue-on-error` to make CI green; repair failing regression tests or production code instead.

The frontend carries inherited ESLint debt. CI compares pull-request findings with the exact base revision and blocks new severity-2 errors without treating inherited findings as permission to grow the baseline.

The branch-matched frontend client is generated from the backend in the same revision. After generation, CI requires `src/FE/src/api/generated` to be clean. This matters because the production frontend image intentionally does not regenerate against a remote dev/prod OpenAPI endpoint — `src/FE/Dockerfile` runs only `npm ci` and `npm run build` — so the client committed in the release SHA must already be the client CI proved against that backend revision.

### External contributor escalation (retired)

The `contributor-quality-gate.yml` workflow that added a separate exact-head owner-approval status for authors other than `rasm105k` has been **removed**, along with its `enforce-owner-review` check. It is no longer a required status, and the reconciler no longer requests it. Merge gating relies on `CI Gate`, CodeQL, browser evidence and explicit merge review. Re-introducing an exact-head owner-approval requirement for external contributors would require adding both a live workflow and a matching required status in `tools/release/configure-github-branch-rules.ps1`.

## Code scanning

GitHub CodeQL **Default setup** is the repository's code-scanning owner.

Do not add `github/codeql-action` jobs to the normal CI while Default setup is enabled. GitHub rejects advanced-configuration uploads when Default setup owns the repository, creating duplicate work and permanently red checks rather than additional protection.

Whether code-scanning findings are merge-blocking is repository security/ruleset state and must be verified in GitHub settings. CI workflow YAML must not duplicate that external control.

## Main verification

The same `CI` workflow runs after a merge to `main`.

Core backend, frontend/API-contract and contract/documentation checks run again against the exact production revision. Code scanning remains owned by GitHub Default setup rather than being duplicated in the CI workflow.

Both application surfaces depend on that exact post-merge evidence:

- The live-app deployment builds the frontend image only after the exact-SHA eligibility adapter has proved a successful `CI Gate` for the checked-out revision. A merge to `main` creates no frontend deployment by itself.
- Azure backend delivery is triggered by the completed `CI` run and validates the triggering run, exact SHA and `CI Gate`; it repeats the current-main check immediately before migrations/deployment so a release that becomes stale during artifact build cannot mutate production.

This prevents frontend/backend drift caused by one surface releasing while the other revision is red, cancelled, stale or still validating. For the live app the frontend and API images are built from one SHA in one run and released as a single Container App revision, so that class of drift cannot occur there at all.

## Production deployment

### Frontend · Azure Container Apps production

`.github/workflows/aca-live-deploy.yml` is named **Deploy Workslip Live App (Container Apps)** and owns frontend production. It is a `workflow_dispatch` workflow, so a merge to `main` never starts a frontend release on its own.

The workflow:

1. refuses any ref other than `refs/heads/main`;
2. checks out the exact selected SHA;
3. runs `node tools/release/verify-production-eligibility.mjs --sha "$GITHUB_SHA"` before any Azure login;
4. builds `workslip-live-app-frontend:<sha>` from `src/FE/Dockerfile` with `az acr build`, passing the Microsoft-login `VITE_AZURE_AD_*` values as build arguments and failing closed when any is missing;
5. builds `workslip-live-app-api:<sha>` from `src/BE/WorkslipApi/Dockerfile.demo` in the same run;
6. deploys both images as one revision of `ca-workslip-live-app` through `src/BE/infrastructure/aca/app.bicep`; and
7. requires `tools/release/post-deploy-smoke.sh` to pass against the public URL before the release is reported successful.

The image is served by nginx, not by a hosting platform's edge. `src/FE/Dockerfile` builds the Vite output and copies it into `nginxinc/nginx-unprivileged` together with `src/FE/nginx.conf`, which owns the SPA fallback (`try_files $uri $uri/ /index.html`), the `/api/` reverse proxy to the API container on `127.0.0.1:5262`, the `/health` passthrough and the frontend cache-control policy. Changing frontend routing, proxying or cache headers means changing `src/FE/nginx.conf` and shipping a new image; there is no dashboard-level override.

The image build does **not** call `generate:api:dev` or fetch OpenAPI from a remote development API. `npm run build` is `tsc -b && npm run typecheck:sw && vite build`. The generated API client is committed and its parity with the same-revision backend contract is a blocking CI check.

Registry access uses the runtime managed identity with `AcrPull`; ACR admin credentials must stay disabled and the workflow verifies both before it deploys. Images are tagged with the exact SHA, so a production revision is always traceable to one validated commit.

The public domain is bound separately. See [Frontend · Production domain](#frontend--production-domain).

### Frontend · Production domain

`.github/workflows/aca-live-cutover.yml` owns `app.mrsoftware.dk`. It is separate from deployment on purpose: shipping a revision and moving customer traffic are different decisions with different rollback semantics.

It runs only from `main` and has three modes:

- `prepare` — reads the Container App FQDN and `customDomainVerificationId` and publishes the required `CNAME app` and `TXT asuid.app` records in the job summary. Non-mutating.
- `bind` — requires confirmation `CUTOVER`, requires `VITE_AZURE_AD_LOGIN_REDIRECT_URI` to equal `https://app.mrsoftware.dk/login`, smoke-tests the deployed Container App first, then adds the hostname and binds a managed TLS certificate with CNAME validation.
- `retire` — requires confirmation `RETIRE_LEGACY`, refuses to run unless `app.mrsoftware.dk` is already bound to `ca-workslip-live-app`, and then stops the legacy App Service.

DNS records are the only step performed outside Azure, and the workflow prints the exact values rather than expecting an operator to remember them.

### Backend · Azure production

`.github/workflows/backend-production-deploy.yml` is named **Backend · Production deploy**. Its normal path listens for completed `CI` runs on `main` and deploys current production. Its manual path is reserved for a no-traffic package deployment to the allowlisted new tenant.

The workflow:

1. validates the triggering workflow is the canonical `CI` run for the exact current `main` SHA and that its `CI Gate` succeeded;
2. builds and packages the API from that exact SHA;
3. re-runs the production eligibility check before any Azure mutation;
4. keeps Azure OIDC permission scoped to the protected deployment job;
5. resolves the dedicated migration identity and applies reviewed migrations;
6. verifies required diagnostics configuration;
7. applies the production release-testing policy;
8. deploys the exact-SHA artifact with bounded retries and captures Azure deployment diagnostics on failure; and
9. requires the API `/health` endpoint to recover and unauthenticated `/api/auth/me` to return 401 before reporting the release successful.

When the target has a `staging` slot, the workflow validates that candidate and
swaps it into production with automatic swap rollback on a failed production
smoke. The retained F1 compatibility path has no slot, so it deploys directly
to production and reports a failed smoke without an automatic App Service
rollback.

The manual new-tenant path additionally requires `main`, the protected `live`
environment, the exact confirmation `DEPLOY NEW TENANT AFTER DATA VERIFIED`,
and the SHA-256 plus allowlisted evidence URL of the reviewed non-personal
SQL/blob comparison manifest. The environment reviewer verifies that evidence;
the workflow records the reference but does not treat a syntactically valid
hash as proof of data correctness. It targets only `api-mrsoftwarev2-live`; it
cannot select current production and it does not move the production domain. The
automatic path remains pinned to `api-mrsoftware-prod` during preparation.

The old ancestor check is intentionally not used. A previously green SHA that is merely contained in a newer `main` is stale and cannot deploy.

### Database · Production migrations

`.github/workflows/database-production-migrations.yml` is the explicit manual migration workflow. The `MIGRATE` confirmation is operator intent, not a validation bypass. Before Azure login or mutation it requires the selected ref to be `main` and the selected exact SHA to pass the same production eligibility gate. Node 24 is set up explicitly before the gate rather than relying on runner-image defaults.

### Infrastructure · Production reconcile

`.github/workflows/infrastructure-production-reconcile.yml` remains the separate privileged infrastructure path. It selects current production or the new tenant from a fixed allowlist, defaults to the non-mutating `plan.ps1` operation, and requires a target/operation-specific confirmation. Before OIDC login it requires `main` and a successful exact-SHA `CI Gate`, then uses the corresponding protected `prod` or `live` environment and verifies the authenticated tenant, subscription, and resource group. Reconcile preserves the dedicated infrastructure identity. Current production also requires the existing API health check; the new foundation cannot claim API readiness until the later data/package gate.

### Production · Readiness smoke

`.github/workflows/production-readiness-smoke.yml` is the maintained public production smoke. It is read-only, but still requires an exact green `main` SHA so evidence cannot accidentally be attached to an unvalidated revision.

Authenticated/destructive Playwright evidence remains blocked until the isolated staging target and approved test authentication are completed; public smoke never substitutes for pre-merge CI.

## Deployment naming and environment ownership

Canonical workflow/job naming is `<Surface> · Production <action>` so GitHub Actions shows both the system and the target directly. Examples are `Backend · Production deploy`, `Database · Production migrations`, `Infrastructure · Production reconcile` and `Production · Readiness smoke`.

Stable cloud resource names are not renamed merely for aesthetics. The active environment inventory is:

- GitHub `prod` — Workslip's protected current Azure application/infrastructure environment. It carries the existing Azure environment configuration and remains the automatic backend target during cutover preparation.
- GitHub `live` — reserved name for the separately protected new-tenant Azure boundary. Manual paths fail closed unless it already exists, allows exactly `main`, requires repository owner `rasm105k` (GitHub user ID `31623093`) as reviewer, and disables administrator bypass. It is not auto-created by a deployment run.
- GitHub `github-pages` — independent GitHub Pages deployment environment.
- GitHub `copilot` — GitHub/Copilot integration environment, not an application production path.
- Azure `ca-workslip-live-app` in `rg-mrsoftwarev2-live` — the Container App serving `app.mrsoftware.dk`. Its frontend and API containers are one revision built from one SHA.
- Azure `api-mrsoftware-prod` in `rg-mrsoftware-prod` — backend production target for `backend-production-deploy.yml`.

Frontend hosting has no environment of its own. There is no hosting dashboard, project setting, preview URL or platform-held token in the frontend release path: the image is built in ACR and deployed by the same Azure workflow as the API.

GitHub may still list `Production` and `Preview` deployment environments created by the retired frontend Git integration. They are historical deployment records, not an active production path, and they are not duplicates of the Azure `prod` or `live` environments. Do not delete or rename an environment from its name alone. Verify usage, deployment records, secrets/variables and external integration ownership first.

## Repository protection

The repository `main` ruleset is defense in depth and must enforce:

- pull request required;
- required status check `CI Gate` (the retired `Feature change guard` and `Contributor Quality Gate` are no longer required);
- no bypass actors;
- direct pushes blocked by the pull-request rule;
- non-fast-forward/force pushes blocked;
- squash-only merge; and
- merge remains an explicit human action.

Legacy or directly configured rulesets are not evidence that the intended policy is active. The reconciler below must be applied and then verified against GitHub; workflow YAML alone does not correct missing status checks or configured bypass actors.

`tools/release/configure-github-branch-rules.ps1` is the authoritative reconciliation command. Its apply payload contains no bypass actors, and its read-back verification now fails if GitHub reports bypass actors, wrong refs/rule types, wrong merge methods, wrong review count, wrong required checks or a non-strict status-check policy. `-VerifyOnly` is the acceptance evidence after an administrator applies the rules.

Production delivery no longer trusts that ruleset as its only red-deploy defense: every maintained production mutation independently fails closed on the exact post-merge `CI Gate`. The ruleset remains necessary to prevent unvalidated code or modified gate logic from being placed on `main` in the first place.

## Production delivery self-test

The delivery implementation itself needs regression protection, because a change to the eligibility adapter is a change to what "deployable" means. The invariants that must hold are:

- the eligibility adapter rejects red, cancelled, stale, missing and duplicate gates;
- frontend production has no `generate:api:dev` dependency and no remote-OpenAPI dependency;
- all privileged production workflows use the shared `workslip-production` lock and an allowlisted protected `prod`/`live` environment;
- manual new-tenant deployment remains exact-main, records the reviewed data-manifest hash as evidence, and cannot move the production domain;
- backend deployment revalidates before mutation and cannot fall back to ancestor semantics;
- the repository-protection source requires `CI Gate` (the retired `Feature change guard` and `Contributor Quality Gate` are no longer required), no bypass actors and strict status checks; and
- retired legacy workflow entrypoints do not reappear.

`tools/release/verify-production-eligibility.test.mjs` covers the first invariant. It is not currently wired into any workflow, and no workflow is named `Production delivery · Self-test`; treat that as an open gap rather than as existing coverage.

## Releases and tags

GitHub tags/releases are optional release-history markers for meaningful product versions. They do not control production deployment and must not recreate a second release pipeline.

## Security

Use GitHub OIDC for Azure deployment. Grant `id-token: write` only to jobs that need Azure tokens. Do not introduce publish profiles, long-lived Azure credentials or privileged repository-writing automation without a concrete requirement and reviewed least-privilege design.

The durable decision behind this model is recorded in [`../architecture/adr/0005-main-as-production-boundary.md`](../architecture/adr/0005-main-as-production-boundary.md).
