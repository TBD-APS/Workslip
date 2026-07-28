# Workslip frontend

React 19, TypeScript and Vite PWA for Workslip.

## Prerequisites

- Node.js 22
- npm
- Workslip API running locally on `http://localhost:5262`, or an explicit API base URL
- outbound access to the pinned Fontsource files on `cdn.jsdelivr.net` when fonts are not already present locally

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
| `npm run typecheck:sw` | Type-check the custom service worker with Web Worker types |
| `npm run lint` | Run ESLint |
| `npm run build` | Synchronize fonts, type-check the app and service worker, and create a production build |
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
- `tsconfig.sw.json` isolates Web Worker types from the browser application type environment.
- `vite.config.ts` defines the local proxy and PWA manifest/build settings.
- `vercel.json` defines the Git deployment policy, production redirects, external API rewrite and response-cache policy.

Authenticated feature routes are loaded through dynamic imports. The login and invite routes remain in the initial application shell; `/app` layout and feature pages are downloaded only after they are rendered. The application entry is emitted as `assets/app-*.js`, while lazy chunks are emitted below `assets/chunks/`, so the PWA precache boundary is deterministic.

A stored authentication token and a successfully loaded current user are separate startup states. `/api/auth/me` has a 12-second request timeout, and authenticated routing transitions to an explicit retry/reload/login recovery screen rather than clearing a potentially valid token or showing an endless spinner.

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

For routing or PWA cache changes, also validate with a clean browser profile:

1. `/login` and invite routes must not request authenticated feature chunks.
2. A representative `/app` route must load its JavaScript and CSS on demand.
3. A route visited once online must remain available on an offline revisit under the supported PWA flow.
4. A deployment with an already-open tab must either keep serving the previously cached lazy chunk or reload once through the guarded `vite:preloadError` recovery path.
5. Service-worker update checks must not overlap while another worker is installing or waiting.
6. A newly deployed worker must activate immediately after discovery and take control without waiting for an update prompt.
7. A temporary `/api/auth/me` outage must retain the stored token and show bounded startup recovery.

## Vercel deployment policy

The Vercel project root must remain `src/FE`.

- Automatic Git deployments are disabled for every branch except `main`.
- Production deployments come from `main`.
- Every push or merge to `main` is eligible for a normal production deployment.
- Manual production redeploys are not filtered by a repository `ignoreCommand`.
- `https://app.mrsoftware.dk` is the canonical production frontend origin.
- `/` is temporarily redirected to `/app` at the Vercel edge before React starts.

Branch suppression is configured through `git.deploymentEnabled` in `vercel.json` with a wildcard deny and an explicit `main` allow. There is no repository-level ignored-build command.

A preview can still be created deliberately through the Vercel dashboard or CLI, but branch pushes do not create previews automatically. A manually created preview uses the production-specific API rewrite in this configuration and must therefore be treated as production-connected. To restore automatic previews, add a reviewed explicit allow rule for the intended branches rather than removing the wildcard deny accidentally.

### DNS

Cloudflare remains the authoritative DNS provider. The `app.mrsoftware.dk` CNAME must point to the Vercel-provided target and remain **DNS only**. Cloudflare must not proxy the app in front of Vercel.

### Production API route

Browsers running on `app.mrsoftware.dk` or a deliberately created `*.vercel.app` deployment use relative `/api/*` URLs regardless of an embedded `VITE_API_BASE_URL`. Vercel rewrites those requests to:

```text
https://api-mrsoftware-prod.azurewebsites.net/api/*
```

The browser therefore keeps a single origin and avoids browser CORS preflight for Workslip API requests. Vercel rewrite caching is explicitly disabled for `/api/*`; authenticated API responses must not be stored at the edge.

The rewrite target is production-specific. A future separate frontend environment must define its own Vercel project configuration or make the upstream target environment-aware before it is enabled.

### Cache and update policy

- SPA HTML: `public, max-age=0, must-revalidate`
- service worker: `public, max-age=0, must-revalidate`
- hashed Vite assets: `public, max-age=31536000, immutable`
- versioned self-hosted fonts: `public, max-age=31536000, immutable`
- API rewrite: CDN caching disabled

The service worker precaches the public bootstrap shell and static assets, but not authenticated route bundles under `assets/chunks/`. Hashed JavaScript and CSS for lazy routes are cached after their first successful request in a stable runtime cache capped at 100 entries. Keeping content-hashed route assets across deployments reduces version-skew failures for routes that were previously visited; a route that has never been visited is not guaranteed to work offline.

Update discovery runs when the service worker registers, when the browser regains connectivity, whenever the app returns to the foreground, and once per minute while the app remains open. Checks are serialized and skipped while another worker is already installing or waiting. `autoUpdate`, `skipWaiting()` and immediate client claiming intentionally activate a discovered deployment without user confirmation.

Vite dynamic-import preload failures trigger one automatic reload per build. Repeated failure in the same build falls through to the normal React error boundary instead of creating a reload loop.

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

The application uses `vite-plugin-pwa` with an injected service worker. The custom service worker is type-checked separately and must not be excluded through `@ts-nocheck` again.

Immediate activation is an accepted product decision recorded in ADR 0002. A deployment may replace an open client without waiting for dirty-form state or explicit confirmation. Runtime chunk retention and one-shot stale-build recovery reduce version-skew failures, but do not make API mutations offline-capable.