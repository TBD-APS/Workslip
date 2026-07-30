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

WOR-213 verified that update discovery and worker activation were not sufficient by themselves. An installed mobile PWA could receive a new worker while the already-open document continued running the old application bundle until the app was fully closed. The lifecycle must therefore include an explicit, guarded reload after an update takes control.

The first visible update implementation let the banner enter `Opdaterer...` before the service-worker coordinator confirmed that a waiting worker existed. It also retained competing navigation owners in the plugin, page, custom worker and fallback timer. Production testing showed that this could leave the installed PWA visually frozen after selecting `Opdater nu`.

## Decision

1. Workslip uses the plugin's `prompt` lifecycle so update activation can be coordinated with application UI.
2. A first-time service-worker installation activates normally and does not reload the page.
3. Update discovery runs:
   - when service-worker registration completes;
   - when the app returns to the foreground;
   - when browser connectivity returns; and
   - once per minute while the app remains open.
4. Update checks are serialized and use no-cache service-worker requests.
5. When a real waiting worker exists, Workslip displays a persistent `Ny version klar` banner with an `Opdater nu` action.
6. Selecting `Opdater nu` asks the service-worker coordinator to apply the update. The banner enters `Opdaterer...` only after the coordinator has accepted an actual waiting worker. If the user does nothing, the same action runs automatically after ten seconds.
7. The coordinator sends `SKIP_WAITING` directly to the waiting worker instead of invoking the plugin's returned update function.
8. The newly activated worker claims clients but does not navigate window clients.
9. One guarded client reload function is the only navigation owner. Both the browser's `controllerchange` event and the plugin's `controlling` callback feed that function, covering browser differences without allowing duplicate reloads.
10. A five-second fallback reload covers installed/mobile browser contexts that omit both expected control-change signals. It shares the same one-shot guard and is cancelled when a normal signal wins.
11. The update banner is informational and cannot permanently dismiss or block the deployment.
12. The public bootstrap shell is precached. Authenticated route chunks are loaded and cached on first use.
13. Previously fetched content-hashed route assets remain in a capped runtime cache across deployments to support already-open clients.
14. A Vite `vite:preloadError` triggers at most one guarded reload per build. A repeated failure reaches the normal application error boundary.
15. The update flow does not wait for shared dirty-form state or require user confirmation.

## Consequences

- Users receive visible confirmation that a new version was detected and can apply it immediately.
- A detected production deployment still applies automatically without a button press or full app close.
- A click cannot disable the update control unless a waiting worker was actually accepted for activation.
- Service-worker activation has one navigation owner instead of worker navigation, plugin direct reload and client reload racing each other.
- Native and plugin lifecycle events provide redundant signals while the shared guard preserves one effective navigation.
- An app that stays open and visible discovers a deployment within approximately one minute, followed by at most a ten-second visible grace period; reopening, refocusing or reconnecting triggers an immediate check.
- The one-shot reload guard and bounded fallback prevent duplicate lifecycle events from creating a reload loop or a permanently applying banner.
- Unsaved in-memory form state may be lost when a deployment reloads the active client. This is an accepted product trade-off.
- Content-hashed lazy chunks and the runtime cache reduce, but cannot eliminate, mixed-version behaviour.
- Service-worker, update-banner and route-splitting changes require clean-profile, already-open-tab, installed-PWA and offline-revisit smoke testing.
- A future change to make updates dismissible or dirty-form-aware requires superseding this ADR.

## Rejected alternatives

- Hourly-only update checks: rejected because fixes should reach an open application faster.
- Mandatory confirmation: rejected because the product owner still requires automatic rollout without user action.
- Invisible automatic activation only: rejected because it gives users no fallback action or confidence that an update was detected.
- Worker-side `WindowClient.navigate`: rejected after production testing because it races client/plugin reload paths and makes effective navigation ownership ambiguous.
- Plugin returned update function plus custom activation/reload handlers: rejected because it obscures which waiting worker is activated and can duplicate control-change handling.
- Letting the plugin call `window.location.reload()` directly: rejected because its navigation would bypass Workslip's shared one-shot guard.
- Two-second fallback: rejected because it can race normal service-worker activation on installed/mobile browsers.
- Reload on first service-worker installation: rejected because there is no previous application version to replace.
- Precache every authenticated route chunk: rejected because it removes the initial-load performance benefit of route splitting.
- Delete all previous route chunks during activation: rejected because already-open clients can still reference the previous deployment's hashed assets.
- Poll more frequently than once per minute: rejected because startup, focus and online events already cover the common immediate-update paths, while tighter polling adds continuous unnecessary traffic.
