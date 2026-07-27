# Workslip frontend

React 19, TypeScript and Vite PWA for Workslip.

## Prerequisites

- Node.js 22
- npm
- Workslip API running locally on `http://localhost:5262`, or an explicit API base URL

## Install and run

```bash
npm ci
npm run dev
```

The development server listens on `http://127.0.0.1:5270`. Requests to `/api` are proxied to `http://localhost:5262` by `vite.config.ts`.

To call a different API, set `VITE_API_BASE_URL` in an uncommitted environment file such as `.env.local`.

## Commands

| Command | Purpose |
|---|---|
| `npm run dev` | Start Vite development server |
| `npm run lint` | Run ESLint |
| `npm run build` | Type-check and create a production build |
| `npm run preview` | Preview the production build |
| `npm run test:vercel-policy` | Test automatic Vercel build/skip decisions |
| `npm run generate:api:local` | Generate the API client using `.env.local` |
| `npm run generate:api:dev` | Generate the API client using `.env.dev` |
| `npm run generate:api:prod` | Generate the API client using `.env.production` |
| `npm run doctor` | Run React Doctor diagnostics |

API generation uses Orval. Generated output must be regenerated from the current OpenAPI contract rather than edited as the contract source.

## Architecture

- `src/routes/` defines application routing and access guards.
- `src/features/` contains feature-oriented UI, hooks and API usage.
- `src/components/` contains shared UI and form components.
- `src/lib/axios.ts` configures the API client, auth header, correlation ID and mutation idempotency header.
- `src/sw.ts` and `src/registerSW.ts` contain service-worker behaviour.
- `vite.config.ts` defines the local proxy and PWA manifest/build settings.

## Form conventions

Follow the repository `AGENTS.md` rules:

- reuse components in `src/components/forms/`;
- use `NumericInput` instead of raw `<input type="number">`;
- normalize Danish decimal comma at the caller boundary;
- keep authorization enforcement on the backend; frontend guards are UX only.

## Validation

```bash
npm ci
npm run lint
npm run build
npm run test:vercel-policy
```

There is currently no general `npm test` script in `package.json`. Do not claim broad frontend test coverage from isolated test files. Add a documented test command when the test runner is standardized.

## Vercel deployment policy

The Vercel project root must remain `src/FE`.

- Standard `rbj--*` work branches do not create automatic preview deployments.
- Production deployments come from `main`.
- A `main` deployment builds only when `src/FE` changed since the last successful production deployment.
- Missing Git SHAs, unavailable history or a failed comparison builds production fail-open.

The policy lives in `vercel.json` and `scripts/vercel-build-policy.mjs`. Vercel interprets exit code `0` as skip and exit code `1` as build. Run `npm run test:vercel-policy` after changing either file.

When a preview is explicitly needed, create it manually from `src/FE` through the Vercel dashboard or CLI. A manual production redeploy can bypass the ignored-build decision in Vercel. Do not weaken the repository policy for a one-off preview.

Rollback is limited to removing `git.deploymentEnabled` and `ignoreCommand` from `vercel.json`; no application or Azure state is involved.

## Environment and secrets

Only variables prefixed with `VITE_` can be embedded into the browser bundle. Never place client secrets, database credentials or privileged tokens in frontend environment files.

Common runtime configuration includes:

- `VITE_API_BASE_URL` — optional API base URL; blank uses the current origin and local Vite proxy.
- Entra/Application Insights settings referenced by frontend source — treat these as public client configuration, not secrets.

## PWA caution

The application uses `vite-plugin-pwa` with an injected service worker. Changes to update/reload, caching, offline drafts or synchronization must be validated against long dirty forms and documented conservatively. A PWA cache is not proof that mutations work offline.
