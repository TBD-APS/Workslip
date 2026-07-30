# ADR 0002: Automatic PWA update with visible fallback

**Status:** Accepted  
**Date:** 2026-07-28  
**Amended:** 2026-07-30
**Owner:** Workslip architecture owner  
**Linear:** WOR-196, WOR-213

## Context

Workslip is deployed as a Vite PWA with an injected custom service worker. Authenticated feature routes are dynamically imported as content-hashed chunks. Previous service-worker behaviour combined automatic registration updates, immediate worker activation and frequent polling without one documented lifecycle contract.

The product owner prioritizes rapid rollout of frontend fixes over preserving unsaved browser form state during a deployment. A mandatory update confirmation would delay rollout and create a second blocking state machine alongside existing routing and form guards.

Immediate activation creates a version-skew risk for clients that loaded application shell code from one deployment and later request a lazy chunk from another deployment. The implementation therefore needs explicit stale-chunk recovery and retention of previously fetched content-hashed route assets.

WOR-213 verified that update discovery and worker activation were not sufficient by themselves. An installed mobile PWA could receive a new worker while the already-open document continued running the old application bundle until the app was fully closed. The lifecycle must therefore include an explicit, guarded navigation or reload after an update is installed.

The first WOR-213 implementation made the update fully automatic but gave users no visible evidence that a deployment had been detected. The accepted follow-up policy adds a visible manual action without making user interaction a prerequisite.

## Decision

1. Workslip uses the plugin's `prompt` lifecycle so update activation can be coordinated with application UI.
2. A first-time service-worker installation activates normally and does not reload the page.
3. Update discovery runs:
   - when service-worker registration completes;
   - when the app returns to the foreground;
   - when browser connectivity returns; and
   - once per minute while the app remains open.
4. Update checks are serialized and use no-cache service-worker requests.
5. When a new worker is waiting, Workslip displays a persistent `Ny version klar` banner with an `Opdater nu` action.
6. Selecting `Opdater nu` activates the waiting worker immediately. If the user does nothing, the same update action runs automatically after ten seconds.
7. The update banner is informational and cannot permanently dismiss or block the deployment.
8. An activating worker records whether it replaces a previous active worker. After claiming clients, an update worker navigates existing window clients to their current URLs; first-time installation does not navigate them.
9. The client also reloads at most once when the plugin reports that the updated worker has taken control or the service-worker controller changes.
10. A two-second fallback reload covers installed/mobile browser contexts that do not reliably emit the expected controller lifecycle event. The fallback is never scheduled for a first-time service-worker installation and is session-guarded per application build.
11. The public bootstrap shell is precached. Authenticated route chunks are loaded and cached on first use.
12. Previously fetched content-hashed route assets remain in a capped runtime cache across deployments to support already-open clients.
13. A Vite `vite:preloadError` triggers at most one guarded reload per build. A repeated failure reaches the normal application error boundary.
14. The update flow does not wait for shared dirty-form state or require user confirmation.

## Consequences

- Users receive visible confirmation that a new version was detected and can apply it immediately.
- A detected production deployment still applies automatically without a button press or full app close.
- Worker-side navigation allows the deployment containing WOR-213 to refresh clients still running the previous client bundle, rather than requiring the new client code to be loaded first.
- An app that stays open and visible discovers a deployment within approximately one minute, followed by at most a ten-second visible grace period; reopening, refocusing or reconnecting triggers an immediate check.
- The one-shot and session guards prevent duplicate lifecycle events or fallback retries from causing a reload loop.
- Unsaved in-memory form state may be lost when a deployment reloads or replaces the active client. This is an accepted product trade-off.
- Content-hashed lazy chunks and the runtime cache reduce, but cannot eliminate, mixed-version behaviour.
- Service-worker, update-banner and route-splitting changes require clean-profile, already-open-tab, installed-PWA and offline-revisit smoke testing.
- A future change to make updates dismissible or dirty-form-aware requires superseding this ADR.

## Rejected alternatives

- Hourly-only update checks: rejected because fixes should reach an open application faster.
- Mandatory confirmation: rejected because the product owner still requires automatic rollout without user action.
- Invisible automatic activation only: rejected because it gives users no fallback action or confidence that an update was detected.
- Rely only on plugin/controller lifecycle events: rejected because the observed installed-PWA failure can leave the old document running after the new worker is available.
- Reload on first service-worker installation: rejected because there is no previous application version to replace.
- Precache every authenticated route chunk: rejected because it removes the initial-load performance benefit of route splitting.
- Delete all previous route chunks during activation: rejected because already-open clients can still reference the previous deployment's hashed assets.
- Poll more frequently than once per minute: rejected because startup, focus and online events already cover the common immediate-update paths, while tighter polling adds continuous unnecessary traffic.
