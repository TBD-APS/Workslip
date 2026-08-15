# Workslip frontend

**Status:** Active  
**Source of truth:** `package.json`, Vite/Vercel configuration and frontend source

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

`npm run dev` generates the local Orval API client before starting Vite, so a fresh checkout does not require a separate generation command. Generation builds the backend OpenAPI document from `src/BE/WorkslipApi` in an isolated contract-generation pass, so it needs neither a running API process nor a database. `.github/actions/generate-frontend-api` runs the same generator and CI requires the generated files to be committed, so the client shipped by Vercel is the same-revision contract CI validated.

`npm run generate:api:live` targets a running API over HTTP instead, using `VITE_API_BASE_URL` from `.env.local` and defaulting to `http://localhost:5262`. Setting `OPENAPI_DOCUMENT` to an already built document skips the backend build in every generation command.

The dev server listens on `http://127.0.0.1:5270`. `/api` is proxied to the local backend by `vite.config.ts` unless an explicit local API base URL is configured, so serving data still requires the API to be running.

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
| `npm run generate:api:dev` | explicit operator/development generation from development environment config; not used by Vercel production |
| `npm run generate:api:prod` | explicit generation from production environment config; not used by the normal Vercel production build |
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
- `vercel.json` — frontend deployment/rewrite/cache policy, including the exact-SHA production eligibility gate.

Use [`AGENTS.md`](AGENTS.md) for frontend implementation conventions. In particular, reuse shared form controls and use `NumericInput` instead of raw number inputs where applicable.

## State, auth and caching

Server state is owned by React Query. Authentication/session changes must clear or isolate user/tenant-sensitive cached state so another user or organization cannot inherit it.

Frontend route/role guards are UX controls only. Backend authorization is the security boundary.

PWA update activation policy is an accepted product decision recorded in [`../../Docs/architecture/adr/0002-immediate-pwa-update-activation.md`](../../Docs/architecture/adr/0002-immediate-pwa-update-activation.md). Inspect the current service-worker code for exact timing/cache mechanics rather than duplicating those values here.

## Environment and secrets

Only `VITE_` values are eligible for inclusion in browser code. Never place client secrets, database credentials, signing keys or privileged tokens in frontend environment files.

`VITE_API_BASE_URL` is for local/non-Vercel API targeting. Local development defaults to `http://localhost:5262`. Vercel-hosted production traffic uses the same-origin `/api` rewrite defined by the deployed frontend configuration.

## Production delivery

Vercel Git deployment is enabled only for `main`. A production deployment record may be created by the Git integration when `main` moves, but the build command first runs `tools/release/verify-production-eligibility.mjs`. It proceeds only when that exact commit is still current `main` and its post-merge `CI Gate` completed successfully.

The verifier uses Vercel's Git metadata and GitHub's public repository API; it does not require a Vercel-held GitHub API token. Red, cancelled, stale, missing or unresolved CI evidence blocks the build. See [`../../Docs/operations/ci-quality-gates.md`](../../Docs/operations/ci-quality-gates.md) for the complete production boundary.

## Validation

```bash
npm ci
npm run lint
npm run test -- --run
npm run build
python ../../tools/docs/check_docs.py
```

For user-visible changes, routing, forms, authentication, session, cache or PWA behaviour, also run the relevant Playwright flow from [`../../Docs/agents/VALIDATION.md`](../../Docs/agents/VALIDATION.md).

Do not infer browser correctness from TypeScript/build success alone.
