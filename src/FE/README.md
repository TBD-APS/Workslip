# Workslip frontend

React 19, TypeScript and Vite PWA for Workslip.

## Prerequisites

- Node.js 22
- npm
- Workslip API running locally on `http://localhost:5262`, or an explicit API base URL
- outbound access to the pinned Fontsource files on `raw.githubusercontent.com` when fonts are not already present locally

## Install and run

```bash
npm ci
npm run dev
```

The development server listens on `http://127.0.0.1:5270`. Requests to `/api` are proxied to `http://localhost:5262` by `vite.config.ts`.

To call a different API locally, set `VITE_API_BASE_URL` in an uncommitted environment file such as `.env.local`.

`npm run dev` and production builds run `scripts/sync-fonts.mjs`. The script downloads the pinned Inter and Outfit variable WOFF2 files into `public/fonts/` only when valid local copies are missing. The generated binaries are ignored by Git; the license notice remains tracked.

## Commands

| Command | Purpose |
|---|---|
| `npm run dev` | Synchronize fonts and start the Vite development server |
| `npm run sync:fonts` | Download and validate the pinned local font assets |
| `npm run lint` | Run ESLint |
| `npm run build` | Synchronize fonts, type-check and create a production build |
| `npm run preview` | Preview the production build |
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
- `src/fonts.css` defines the same-origin Inter and Outfit font faces.
- `scripts/sync-fonts.mjs` materializes pinned WOFF2 files before development and production builds.
- `src/sw.ts` and `src/registerSW.ts` contain service-worker behaviour.
- `vite.config.ts` defines the local proxy and PWA manifest/build settings.
- `vercel.json` defines production redirects, the external API rewrite and response-cache policy.

## Form conventions

Follow the repository `AGENTS.md` rules:

- reuse components in `src/components/forms/`;
- use `NumericInput` instead of raw `<input type="number">`;
- normalize Danish decimal comma at the caller boundary;
- keep authorization enforcement on the backend; frontend guards are UX only.

## Validation

```bash
npm ci
npm run sync:fonts
npm run lint
npm run build
```

There is currently no general `npm test` script in `package.json`. Do not claim broad frontend test coverage from isolated test files. Add a documented test command when the test runner is standardized.

## Vercel deployment policy

The Vercel project root must remain `src/FE`.

- Standard `rbj--*` work branches do not create automatic preview deployments.
- Production deployments come from `main`.
- Every push or merge to `main` is eligible for a normal production deployment.
- Manual production redeploys are not filtered by a repository `ignoreCommand`.
- `https://app.mrsoftware.dk` is the canonical production frontend origin.
- Requests to the public production alias `https://workslip-v2-0.vercel.app` are permanently redirected to the equivalent path on the canonical origin.
- `/` is temporarily redirected to `/app` at the Vercel edge before React starts.

Preview suppression is configured through `git.deploymentEnabled` in `vercel.json`. There is no repository-level ignored-build command.

When a preview is explicitly needed, create it manually from `src/FE` through the Vercel dashboard or CLI. To restore automatic preview deployments for standard work branches, remove the `git.deploymentEnabled` rule from `vercel.json`.

### DNS

Cloudflare remains the authoritative DNS provider. The `app.mrsoftware.dk` CNAME must point to the Vercel-provided target and remain **DNS only**. Cloudflare must not proxy the app in front of Vercel.

### Production API route

Browsers running on `app.mrsoftware.dk` or a `*.vercel.app` deployment use relative `/api/*` URLs regardless of an embedded `VITE_API_BASE_URL`. Vercel rewrites those requests to:

```text
https://api-mrsoftware-prod.azurewebsites.net/api/*
```

The browser therefore keeps a single origin and avoids browser CORS preflight for Workslip API requests. Vercel rewrite caching is explicitly disabled for `/api/*`; authenticated API responses must not be stored at the edge.

The rewrite target is production-specific. A future separate frontend environment must define its own Vercel project configuration or make the upstream target environment-aware before it is enabled.

### Cache policy

- SPA HTML: `public, max-age=0, must-revalidate`
- service worker: `public, max-age=0, must-revalidate`
- hashed Vite assets: `public, max-age=31536000, immutable`
- versioned self-hosted fonts: `public, max-age=31536000, immutable`
- API rewrite: CDN caching disabled

## Environment and secrets

Only variables prefixed with `VITE_` can be embedded into the browser bundle. Never place client secrets, database credentials or privileged tokens in frontend environment files.

Common runtime configuration includes:

- `VITE_API_BASE_URL` — optional local/non-Vercel API base URL and OpenAPI-generation source. Vercel-hosted runtime traffic uses the same-origin `/api` rewrite instead.
- Entra/Application Insights settings referenced by frontend source — treat these as public client configuration, not secrets.

## Microsoft login callback state

The browser PKCE flow stores the complete temporary login state in `sessionStorage` under `workslip.loginPkce` before navigating to Microsoft. The stored object includes the OAuth state value, PKCE verifier, redirect URI and return target.

Do not replace this with an in-memory map or store only an opaque reference. Microsoft login performs a full-page navigation, which destroys module memory before the callback is processed. Invalid or legacy stored values must be discarded rather than used for token exchange.

The login route clears the PKCE state after success, cancellation or callback failure. Never persist the verifier in `localStorage`, logs, telemetry or URL parameters.

## PWA caution

The application uses `vite-plugin-pwa` with an injected service worker. Changes to update/reload, caching, offline drafts or synchronization must be validated against long dirty forms and documented conservatively. The self-hosted WOFF2 files are included in the precache manifest, but a PWA cache is not proof that API mutations work offline.
