# ADR 0018: Frontend production is an nginx container in Azure Container Apps

**Status:** Accepted  
**Date:** 2026-09-03  
**Decision owners:** Workslip maintainers  
**Supersedes:** ADR 0005 where it makes Vercel the frontend hosting mechanism; ADR 0001 decision 8 (Vercel cache-purge credential boundary)

## Context

ADR 0005 made `main` the single production boundary and gated production on an
exact-SHA green `CI Gate`. At that time the frontend was hosted by Vercel
through its Git integration, and the backend was deployed to Azure by GitHub
Actions. Two consequences followed from having two hosting platforms:

1. the exact-SHA gate needed a second implementation,
   `src/FE/scripts/vercel-production-eligibility.mjs`, physically inside
   `src/FE` because a configured Vercel Root Directory can prevent a build from
   reading repository files above it; and
2. `src/FE/vercel.json` owned frontend routing, the `/api/*` proxy and the
   cache-control policy, none of which are visible to the Azure deployment.

The frontend has since moved onto the same platform as the API.
`.github/workflows/aca-live-deploy.yml` builds `src/FE/Dockerfile` into
`workslip-live-app-frontend:<sha>` in Azure Container Registry and deploys it,
together with `workslip-live-app-api:<sha>`, as one revision of the
`ca-workslip-live-app` Container App. `aca-live-cutover.yml` binds
`app.mrsoftware.dk` to that Container App. `app.mrsoftware.dk` answers with
`server: nginx/1.27.5` and no external-hosting edge headers, and the only nginx
in the repository is `src/FE/Dockerfile`, which runs
`nginxinc/nginx-unprivileged:1.27-alpine` with `src/FE/nginx.conf`.

The written record had not caught up. `vercel.json` still looked authoritative,
which mattered because it carried a cache-control policy that a container-served
frontend does not inherit: `/index.html` and `/sw.js` at
`max-age=0, must-revalidate`, `/assets/*` and `/fonts/*.woff2` at
`max-age=31536000, immutable`. For a PWA that policy is not decoration — a
long-cached `index.html` or a stale `sw.js` is how a client gets stranded on an
old build.

## Decision

Frontend production is a container image, built and released by the same Azure
workflow as the API.

1. `src/FE/Dockerfile` is the frontend production build. It runs `npm ci` and
   `npm run build`, then copies `dist/` into `nginxinc/nginx-unprivileged`
   together with `src/FE/nginx.conf`.
2. `src/FE/nginx.conf` is the single source of truth for frontend serving
   behaviour: SPA fallback, the `/api/` reverse proxy to the API container on
   `127.0.0.1:5262`, the `/health` passthrough, security headers, and the
   cache-control policy. It must keep `/index.html` and the service worker on a
   revalidating policy and may serve content-hashed assets immutably.
3. `.github/workflows/aca-live-deploy.yml` is the only frontend production path.
   It runs from `main` only, calls
   `tools/release/verify-production-eligibility.mjs` before any Azure login, and
   tags both images with the exact SHA.
4. `tools/release/verify-production-eligibility.mjs` is the **only** production
   eligibility adapter. The second, platform-local adapter is retired. A
   frontend release now reads the same evidence, from the same checkout, as
   every other production mutation.
5. `.github/workflows/aca-live-cutover.yml` owns `app.mrsoftware.dk`.
   Deploying a revision and moving customer traffic stay separate operations
   with separate confirmations.
6. Frontend hosting holds no credentials, project settings or dashboard state.
   There is no hosting account to configure, no preview URL, and no external
   cache to purge.
7. No Vercel-owned hostname remains in a deployed contract. Redirect URIs come
   from `src/BE/infrastructure/deploy-entra.ps1` and allowed origins from
   `src/BE/infrastructure/staticConfig.bicep`; both are template state that must
   be reconciled against the live tenant and App Configuration explicitly.

## Consequences

### Positive

- One platform, one eligibility adapter, one interpretation of "deployable".
- Frontend and API ship from one SHA as one Container App revision, so
  frontend/backend contract drift cannot be produced by two platforms releasing
  independently.
- Frontend serving behaviour is reviewable in the repository and versioned with
  the image, instead of partly living in a mutable hosting dashboard.
- A merge to `main` creates no frontend deployment by itself: `aca-live-deploy.yml`
  is a `workflow_dispatch`. The "a deployment record exists while CI is pending"
  state that ADR 0005 had to reason about is gone.
- Registry access uses the runtime managed identity with `AcrPull`, and ACR
  admin credentials stay disabled and are verified each run.

### Trade-offs

- Frontend releases are now explicit operator actions rather than an automatic
  consequence of a merge. Shipping requires dispatching the workflow.
- Cache-control correctness is now Workslip's own responsibility in
  `nginx.conf`. A hosting platform's defaults no longer provide a floor, and a
  mistake here strands PWA clients on a stale build. This is the single highest
  risk introduced by the move and belongs in review whenever `nginx.conf`
  changes.
- The frontend build is slower, because it is an image build in ACR rather than
  an incremental platform build.
- `/api/` is proxied inside the revision to `127.0.0.1:5262`, so the frontend and
  API containers scale together as one unit.

## Rejected alternatives

- **Keep Vercel and keep both adapters.** Rejected because production already
  serves from nginx; retaining `vercel.json` and its adapter leaves a file that
  looks authoritative while serving nothing, and leaves the cache policy in a
  place that is not applied.
- **Delete `vercel.json` without porting its cache policy.** Rejected because
  the policy is the load-bearing part of that file for a PWA. The removal must
  move those rules into `nginx.conf`, not drop them.
- **Put a CDN or Front Door in front of the Container App to recover an edge
  cache.** Rejected for now: nothing currently depends on edge caching, and
  adding a cache layer would reintroduce a purge operation and a second place
  where cache headers are decided. It would need its own ADR.
- **Serve the SPA from the ASP.NET Core API as static files.** Rejected because
  it couples frontend asset delivery to the API's request pipeline and removes
  the ability to reason about, and scale, the two containers separately.
- **Rewrite ADR 0005 and ADR 0001 to describe today's hosting.** Rejected
  because an accepted ADR records what was decided and why at that time.
  Editing their reasoning would erase the decision trail; they are annotated
  with a partial supersession pointing here instead.
