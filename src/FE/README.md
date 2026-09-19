# Workslip frontend

**Status:** Active  
**Source of truth:** `package.json`, Vite configuration, `nginx.conf` and frontend source

React 19 + TypeScript + Vite PWA frontend.

## Prerequisites

- Node.js 24 recommended to match release CI
- npm
- .NET SDK 10, used to generate the API contract from the backend project in this working tree
- A running Workslip API on `http://localhost:5262`, or an explicit local API base URL, only to serve requests from the running frontend

## Run locally

```bash
npm ci
npm run dev
```

`npm run dev` generates the local Orval API client before starting Vite, so a fresh checkout does not require a separate generation command. Generation builds the backend OpenAPI document from `src/BE/WorkslipApi` in an isolated contract-generation pass, so it needs neither a running API process nor a database. `.github/actions/generate-frontend-api` runs the same generator and CI requires the generated files to be committed, so the client shipped in the deployed container image is the same-revision contract CI validated.

`npm run generate:api:live` targets a running API over HTTP instead, using `VITE_API_BASE_URL` from `.env.local` and defaulting to `http://localhost:5262`. Setting `OPENAPI_DOCUMENT` to an already built document skips the backend build in every generation command.

The dev server listens on `http://127.0.0.1:5270`. `/api` is proxied to the local backend by `vite.config.ts` unless an explicit local API base URL is configured, so serving data still requires the API to be running.

For the canonical full-stack bootstrap and physical-phone testing, run these commands from the repository root instead of editing the Vite host permanently:

```powershell
.\dev.ps1
.\dev.ps1 -Mobile
```

The root bootstrap forces the Vite child process to use same-origin API requests, so ignored `.env.local` values cannot bypass the canonical `/api` proxy. `-Mobile` additionally overrides the Vite listener to the LAN for that process only and prints the phone URL. The backend stays on localhost and phone API traffic uses the existing `/api` proxy. See [`../../Docs/operations/local-development.md`](../../Docs/operations/local-development.md) for network, firewall and secure-context guidance.

Manual `npm run dev` remains a focused frontend path and may intentionally honor an explicit `VITE_API_BASE_URL`; it is not the canonical full-stack or phone-testing path.

## Commands

The authoritative command list is `package.json`.

| Command | Purpose |
|---|---|
| `npm run dev` | generate the local API client, sync required font assets and start Vite |
| `npm run lint` | ESLint |
| `npm run test -- --run` | run Vitest once |
| `npm run build` | production type-check/build including service worker; does not fetch a remote OpenAPI document |
| `npm run preview` | preview the production build |
| `npm run generate:api:local` | generate API client from the backend OpenAPI contract built in this working tree |
| `npm run generate:api:live` | generate API client from a running API; defaults to `http://localhost:5262` |
| `npm run generate:api:dev` | explicit operator/development generation from development environment config; not used by the deployed build |
| `npm run generate:api:prod` | explicit generation from production environment config; not used by the deployed build |
| `npm run typecheck:sw` | type-check the service worker |
| `npm run sync:fonts` | materialize pinned local font files |

Generated API code is derived from OpenAPI and must not be edited as the contract source. When backend contract changes affect generated output, run `npm run generate:api:local` and commit the result; CI fails if branch-matched generation leaves `src/api/generated` dirty.

## Architecture landmarks

- `src/routes/` — application routing and route guards.
- `src/features/` — feature-oriented UI and API usage.
- `src/components/` — shared UI/form components.
- `src/providers/` — application/session providers.
- `src/lib/axios.ts` — shared API transport/auth/correlation behaviour.
- `src/sw.ts` and `src/registerSW.ts` — service-worker/update behaviour.
- `vite.config.ts` — Vite/PWA/local proxy configuration.
- `nginx.conf` and `demo-nginx.conf` — how the built app is actually served: SPA fallback, same-origin `/api/` proxy, cache-control policy and security headers.
- `Dockerfile` and `Dockerfile.demo` — the container images that build `dist/` and serve it through those nginx configurations.

Use [`AGENTS.md`](AGENTS.md) for frontend implementation conventions. In particular, reuse shared form controls and use `NumericInput` instead of raw number inputs where applicable.

## State, auth and caching

Server state is owned by React Query. Authentication/session changes must clear or isolate user/tenant-sensitive cached state so another user or organization cannot inherit it.

Frontend route/role guards are UX controls only. Backend authorization is the security boundary.

PWA update activation policy is an accepted product decision recorded in [`../../Docs/architecture/adr/0002-immediate-pwa-update-activation.md`](../../Docs/architecture/adr/0002-immediate-pwa-update-activation.md). Inspect the current service-worker code for exact timing/cache mechanics rather than duplicating those values here.

## Environment and secrets

Only `VITE_` values are eligible for inclusion in browser code. Never place client secrets, database credentials, signing keys or privileged tokens in frontend environment files.

`VITE_API_BASE_URL` is for local API targeting. Manual frontend development may use it explicitly. The canonical root `dev.ps1` path forces same-origin API traffic through Vite's `/api` proxy instead. Deployed traffic is same-origin too: the image is built with `VITE_API_BASE_URL=/` and `nginx.conf` proxies `/api/` to the API.

## Production delivery

The live frontend is served as a container image. `Dockerfile` builds `dist/` with `npm run build` and serves it from `nginxinc/nginx-unprivileged` using `nginx.conf`, which owns the SPA fallback, the same-origin `/api/` proxy, the cache-control policy and the security headers. `.github/workflows/aca-live-deploy.yml` builds that image and deploys it to Azure Container Apps. The demo environment is the same build served by `demo-nginx.conf` from `Dockerfile.demo`, deployed by `.github/workflows/demo-container-apps-deploy.yml`.

No branch deploys the frontend automatically. `aca-live-deploy.yml` is `workflow_dispatch` only, refuses any ref other than `refs/heads/main`, and then runs `tools/release/verify-production-eligibility.mjs --sha "$GITHUB_SHA"` with the workflow token. That verifier is the single production eligibility contract shared by every deployable surface: it proceeds only when the selected commit is still current `main` and the `CI Gate` job of `frontend-validation.yml` completed successfully for that exact SHA. It is called without `--wait-seconds`, so it does not wait for an in-flight run — red, cancelled, stale, missing or still-running CI evidence blocks the deploy immediately. Feature, pull-request, hotfix, validation and release-candidate branches are validated by GitHub Actions only.

A green `main` therefore does not by itself mean production moved, because the deploy is an explicit dispatch. Confirm rollout from the live Container Apps revision and the image tag it is running, not from GitHub merge or CI state alone.

### Caching contract

`nginx.conf` is where frontend cache behaviour lives. Both serving configurations apply the same policy:

| Path | Policy |
|---|---|
| `/index.html`, and every SPA route that reaches it through the fallback | `public, max-age=0, must-revalidate` |
| `/sw.js` | `public, max-age=0, must-revalidate` |
| `/robots.txt` | `text/plain; charset=utf-8` and `public, max-age=0, must-revalidate` |
| `/assets/**` — content-hashed JS/CSS including `assets/chunks/` | `public, max-age=31536000, immutable`; an unknown hash returns 404 instead of the SPA shell |
| `/fonts/*.woff2` — version-pinned filenames from `npm run sync:fonts` | `public, max-age=31536000, immutable` |
| `/manifest.webmanifest` | served as `application/manifest+json`, which nginx's bundled `mime.types` does not know |

The navigation shell and the service worker must revalidate, or an installed PWA client can stay pinned to a build whose hashed assets no longer exist. Note that nginx replaces rather than merges an inherited `add_header` set: every location that adds a `Cache-Control` repeats the three server-level security headers, and a new location that omits them silently drops them for that path.

## Validation

```bash
npm ci
npm run lint
npm run test -- --run
npm run build
node --test ../../tools/release/verify-production-eligibility.test.mjs
python ../../tools/docs/check_docs.py
```

For user-visible changes, routing, forms, authentication, session, cache or PWA behaviour, also run the relevant Playwright flow from [`../../Docs/agents/VALIDATION.md`](../../Docs/agents/VALIDATION.md).

Do not infer browser correctness from TypeScript/build success alone.
