# ADR 0002: Immediate PWA update activation

**Status:** Accepted  
**Date:** 2026-07-28  
**Amended:** 2026-07-30  
**Owner:** Workslip architecture owner  
**Linear:** WOR-196, WOR-213

## Context

Workslip is deployed as a Vite PWA with an injected custom service worker. Authenticated feature routes are dynamically imported as content-hashed chunks. Previous service-worker behaviour combined automatic registration updates, immediate worker activation and frequent polling without one documented lifecycle contract.

The product owner prioritizes rapid rollout of frontend fixes over preserving unsaved browser form state during a deployment. Update prompts and dirty-form-aware activation would delay rollout and add a second update state machine alongside the existing routing and form guards.

Immediate activation creates a version-skew risk for clients that loaded application shell code from one deployment and later request a lazy chunk from another deployment. The implementation therefore needs explicit stale-chunk recovery and retention of previously fetched content-hashed route assets.

WOR-213 verified that update discovery and worker activation were not sufficient by themselves. An installed mobile PWA could receive a new worker while the already-open document continued running the old application bundle until the app was fully closed. The client lifecycle must therefore include an explicit, guarded reload after an update is installed.

## Decision

1. Workslip uses `registerType: 'autoUpdate'`.
2. A newly installed service worker calls `skipWaiting()` and claims clients immediately.
3. Update discovery runs:
   - when service-worker registration completes;
   - when the app returns to the foreground;
   - when browser connectivity returns; and
   - once per minute while the app remains open.
4. Update checks are serialized. A waiting worker is explicitly sent a `SKIP_WAITING` message instead of leaving the client on the previous version.
5. A client that was already controlled when the page loaded reloads at most once when the plugin reports an update, the service-worker controller changes, or the installing worker activates.
6. A two-second fallback reload covers installed/mobile browser contexts that do not reliably emit the expected controller lifecycle event. The fallback is never scheduled for a first-time service-worker installation.
7. The public bootstrap shell is precached. Authenticated route chunks are loaded and cached on first use.
8. Previously fetched content-hashed route assets remain in a capped runtime cache across deployments to support already-open clients.
9. A Vite `vite:preloadError` triggers at most one guarded reload per build. A repeated failure reaches the normal application error boundary.
10. The update flow does not wait for shared dirty-form state or user confirmation.

## Consequences

- A detected production deployment activates and reloads an already-open controlled client without requiring a button press or full app close.
- An app that stays open and visible discovers a deployment within at most approximately one minute; reopening, refocusing or reconnecting triggers an immediate check.
- The one-shot guard prevents duplicate lifecycle events from causing a reload loop.
- Unsaved in-memory form state may be lost when a deployment reloads or replaces the active client. This is an accepted product trade-off.
- Content-hashed lazy chunks and the runtime cache reduce, but cannot eliminate, mixed-version behaviour.
- Service-worker and route-splitting changes require clean-profile, already-open-tab, installed-PWA and offline-revisit smoke testing.
- Prompt-based dirty-safe updates are not a prerequisite for WOR-196 or WOR-213. A future change to that policy requires superseding this ADR.

## Rejected alternatives

- Hourly-only update checks: rejected because fixes should reach an open application faster.
- Prompt-based activation: rejected because the product owner prefers immediate rollout over preserving unsaved in-memory state.
- Rely only on plugin/controller lifecycle events: rejected because the observed installed-PWA failure can leave the old document running after the new worker is available.
- Reload on first service-worker installation: rejected because there is no previous application version to replace.
- Precache every authenticated route chunk: rejected because it removes the initial-load performance benefit of route splitting.
- Delete all previous route chunks during activation: rejected because already-open clients can still reference the previous deployment's hashed assets.
- Poll more frequently than once per minute: rejected because startup, focus and online events already cover the common immediate-update paths, while tighter polling adds continuous unnecessary traffic.
