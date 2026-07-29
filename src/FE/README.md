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
- `src/providers/AuthContext.tsx` provides the lightweight public auth contract and loads login helpers only when used.
- `src/providers/AuthenticatedAppProvider.tsx` owns React Query, the generated current-user client, push registration and authenticated auth state; it is loaded only when a stored token exists.
- `src/routes/preloadPrimaryAppRoute.ts` warms the authenticated layout and default jobs route only after a token exists, allowing their code to download alongside session validation without affecting the anonymous login path.
- `src/features/jobs/queries/jobListQuery.ts` owns the default jobs query request, prefetch, cache lifetime and initial browser-state key.
- `src/hooks/paginatedListState.ts` keeps paginated list storage and query-key construction consistent between prefetching and rendered lists.
- `src/base.css` contains the small global reset, variables and shared public controls.
- `src/public-*.css` contains only login, invitation, recovery, error and public paint/font rules.
- `src/authenticated-base.css` defines authenticated-only globals and the Inter/Outfit font faces.
- `src/App.css` contains the authenticated application styling and is imported only by the lazy `AppLayout` boundary.
- `src/features/auth/components/OneTimeCodeLogin.tsx` isolates React Hook Form, Zod and one-time-code API code from the default passkey screen.
- `public/robots.txt` is the static crawler policy for the authenticated app domain and must not be handled by the SPA rewrite.
- `scripts/sync-fonts.mjs` materializes pinned WOFF2 files before development and production builds.
- `src/sw.ts` and `src/registerSW.ts` contain service-worker behaviour.
- `tsconfig.sw.json` isolates Web Worker types from the browser application type environment.
- `vite.config.ts` defines the local proxy and PWA manifest/build settings.
- `vercel.json` defines the Git deployment policy, production rewrites and response-cache policy.

Authenticated feature routes and invitation enrollment are loaded through dynamic imports. The default passkey login remains in the initial application shell. The application entry is emitted as `assets/app-*.js`, while lazy chunks are emitted below `assets/chunks/`, so the PWA precache boundary is deterministic.

The public shell does not import the old marketing stylesheet, authenticated application CSS, branded web fonts, one-time-code form dependencies, invitation enrollment, React Query, generated authenticated clients or axios. It uses the system font and static background effects. A stored token loads `AuthenticatedAppProvider`, which installs QueryClient context and resolves `/api/auth/me`; at the same time, the authenticated layout and default jobs route are warmed in parallel. Optional one-time-code/dev-login actions load their API module only when invoked.

After `/api/auth/me` succeeds, the default jobs query is prefetched with the same status, search and sort key used by the rendered list. Jobs data is fresh for 30 seconds and retained in memory for 30 minutes, so revisiting `/app` displays cached rows immediately and revalidates stale data in the background. This cache is not persisted to IndexedDB or local storage and is cleared on logout to prevent data crossing user sessions.

The SPA root is served directly without a Vercel redirect. The client router renders login for unauthenticated users and moves an already authenticated user to `/app`.

Service-worker registration is scheduled after the initial window load and an idle callback. Application Insights, Vercel Analytics, Speed Insights and the Sonner toaster are loaded after the first pointer/keyboard interaction or a ten-second fallback, then scheduled during idle time. The Application Insights SDK itself remains a dynamic import. Once the service worker registers, the accepted immediate update-discovery and activation policy remains unchanged.

A stored authentication token and a successfully loaded current user are separate startup states. `/api/auth/me` has a six-second request timeout, and authenticated routing shows the explicit retry/reload/login recovery screen after the same six-second grace period rather than clearing a potentially valid token or showing an endless spinner.

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

For routing, performance or PWA cache changes, also validate with a clean browser profile:

1. `/` must return the SPA document directly rather than redirecting to `/app`.
2. `/robots.txt` must return plain text containing valid robots directives and must never return the SPA HTML document.
3. An unauthenticated `/login` visit must not request authenticated feature chunks, invitation enrollment, `App.css`, Inter/Outfit, React Hook Form, Zod, Sonner, telemetry SDKs, React Query, generated authenticated clients or axios before interaction.
4. Opening the one-time-code option must load and render its isolated form/API chunk.
5. Invitation routes must load their isolated route chunk and remain fully styled.
6. Login, invitation, startup recovery and public error states must remain fully styled and responsive without inherited color/background transitions on their root containers.
7. A stored-token `/app` visit must start downloading `AuthenticatedAppProvider`, `AppLayout` and `JobList` in parallel with session validation, then load the existing jobs query without duplicate route downloads.
8. After `/api/auth/me` succeeds, the initial jobs query must use the exact key later consumed by `JobList`; an already-running request must be deduplicated.
9. Revisiting Jobs within 30 minutes must show cached data immediately; after 30 seconds it must refresh in the background without replacing rows with the full-page skeleton.
10. Logout must clear React Query data before another user can authenticate in the same browser.
11. Login, dev login, user updates, push registration and current-user retry must retain their existing behaviour.
12. Application Insights, Vercel Analytics, Speed Insights and service-worker registration must not block the first render.
13. Service-worker installation must not proactively download application CSS, fonts, images or lazy chunks.
14. A route visited once online must remain available on an offline revisit under the supported PWA flow.
15. A deployment with an already-open tab must either keep serving the previously cached lazy chunk or reload once through the guarded `vite:preloadError` recovery path.
16. Service-worker update checks must not overlap while another worker is installing or waiting.
17. A newly deployed worker must activate immediately after discovery and take control without waiting for an update prompt.
18. A temporary `/api/auth/me` outage must retain the stored token and show recovery within six seconds.
19. A production Lighthouse rerun must confirm the generated public critical path rather than relying only on source inspection.

## Vercel deployment policy

The Vercel project root must remain `src/FE`.

- Automatic Git deployments are disabled for every branch except `main`.
- Production deployments come from `main`.
- Every push or merge to `main` is eligible for a normal production deployment.
- Manual production redeploys are not filtered by a repository `ignoreCommand`.
- `https://app.mrsoftware.dk` is the canonical production frontend origin.
- `/` is handled by the SPA rewrite; the router decides between login and the authenticated app without an HTTP redirect.
- `/robots.txt` is excluded from the SPA rewrite and disallows crawler indexing of the authenticated app domain.

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
- robots policy: `text/plain; charset=utf-8`, `public, max-age=0, must-revalidate`, `nosniff`
- service worker: `public, max-age=0, must-revalidate`
- hashed Vite assets: `public, max-age=31536000, immutable`
- versioned self-hosted fonts: `public, max-age=31536000, immutable`
- authenticated jobs HTTP responses: private browser revalidation through response-complete ETags; no CDN caching
- jobs React Query data: 30-second freshness, 30-minute in-memory retention, clear on logout
- API rewrite: CDN caching disabled

The service worker precaches only the SPA document, web manifest and bootstrap JavaScript. CSS, fonts, images and lazy chunks are cached only after the browser requests them. Same-origin assets below `/assets/` and `/fonts/` use the stable capped runtime cache. This prevents a public login visit from downloading the authenticated application during service-worker installation while retaining offline revisits for resources that were actually used.

Registration is deferred until after the initial page load and an idle callback. Update discovery then runs when the service worker registers, when the browser regains connectivity, whenever the app returns to the foreground, and once per minute while the app remains open. Checks are serialized and skipped while another worker is already installing or waiting. `autoUpdate`, `skipWaiting()` and immediate client claiming intentionally activate a discovered deployment without user confirmation.

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
