---
title: 'Desktop-only Superadmin organization sessions'
type: 'feature'
created: '2026-07-30'
status: 'done'
baseline_commit: '9ef939552b6b865f42ffe6805fc6b4ac0116bec8'
context:
  - '{project-root}/Docs/api/contract.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Superadmins can currently manage organizations and continue delegated customer-tenant sessions from iOS or Android. Those privileged workflows are intended only for a computer, and a persisted delegated token must never let a mobile client briefly enter tenant content.

**Approach:** Treat desktop as the existing device-family concept, hardened for iPadOS: block Superadmin workflows on iOS/Android before authentication bootstrap consumes a saved token, recover safely to the platform organization, and show a stable Danish desktop-only screen. A narrow window on a desktop OS remains supported.

## Boundaries & Constraints

**Always:** Apply one shared synchronous platform policy to direct `/superadmin` access, normal Superadmin login routing, delegated `/app` access, navigation affordances, and organization-session creation/activation. On an unsupported device, restore a valid saved home token and remove delegation metadata before `/api/auth/me` or tenant queries run; if recovery state is incomplete or corrupt, clear authentication and require login. Harden detection for iPadOS reporting a Macintosh user agent. Keep ordinary non-Superadmin mobile use unchanged. Display a non-looping authenticated blocker with “Superadmin er kun tilgængelig på computer” and a logout action, and issue no organization-management/session requests from it.

**Ask First:** Changing “desktop” to a viewport breakpoint; adding backend device enforcement or token claims; changing delegated-token semantics; integrating the separate Playwright branch; broad authentication-storage cleanup unrelated to Superadmin sessions.

**Never:** Rely only on CSS hiding, redirect `/superadmin` and `/app` into a loop, leave a delegated customer token active on mobile, briefly render customer content before blocking, trust inconsistent session metadata, block normal mobile users, or represent this frontend product boundary as tamper-proof security.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Desktop home | Superadmin home token on desktop | Dashboard and organization actions work unchanged | Existing errors apply |
| Mobile home | Superadmin home token on iOS/Android | Desktop-only screen; no organization API calls | Logout remains available |
| Mobile delegated | Delegated token plus valid home state | Restore home token and clear delegation before auth bootstrap; show blocker | Never render tenant content |
| Broken delegated recovery | Delegated token with missing/corrupt home state | Clear auth and delegation; require login | Do not continue with delegated token |
| Stale metadata | Home token plus obsolete delegation keys | Clear delegation metadata; retain home login and show blocker | No tenant request |
| Ordinary mobile user | Non-Superadmin token on iOS/Android | Existing mobile application remains available | Existing errors apply |
| Narrow desktop | Desktop OS below 768px width | Superadmin remains available | Responsive layout applies |

</frozen-after-approval>

## Code Map

- `src/FE/src/lib/platform.ts` -- shared device-family detection; add iPadOS handling and desktop eligibility.
- `src/FE/src/features/superadmin/api.ts` -- reject organization-management requests outside desktop.
- `src/FE/src/features/superadmin/organizationSession.ts` -- delegated-token inspection, identity-bound/unexpired recovery, cross-tab-safe cleanup, activation defense.
- `src/FE/src/main.tsx` -- run synchronous persisted-session recovery before React/AuthProvider initialization.
- `src/FE/src/routes/index.tsx` -- protect direct Superadmin routing with a desktop-only boundary.
- `src/FE/src/components/layouts/AppLayout.tsx` -- desktop-aware redirects and navigation affordances.
- `src/FE/src/features/superadmin/routes/SuperAdmin.tsx` -- session-request defense in depth.
- `src/FE/src/features/superadmin/routes/SuperAdmin.css` -- desktop-only blocker presentation.
- `src/FE/package.json`, `src/FE/vite.config.ts`, `src/FE/src/test/setup.ts` -- focused Vitest/jsdom test harness.

## Tasks & Acceptance

**Execution:**
- [x] Platform/session modules -- implement the shared device rule and pre-bootstrap normalization as an idempotent operation. Restore only an unexpired home Superadmin JWT whose actor ID and `organizationId` match the delegated JWT's actor ID and `homeOrganizationId`; missing/malformed/inconsistent claims fail closed. Re-check shared storage before destructive cleanup so concurrent tabs cannot erase a token another tab just restored.
- [x] Router/layout/Superadmin route -- block unsupported Superadmins without loops, flashes, or API calls while preserving ordinary mobile navigation; keep `AppLayout` independently fail-closed if composed without the route boundary.
- [x] Frontend tests -- cover device detection, claim/expiry consistency, malformed active state, concurrent normalization, and every recovery branch in the edge-case matrix; exercise both mobile and desktop boundary paths and zero organization calls.
- [x] API contract -- document that valid recovery shows the blocker, invalid recovery requires login, and desktop-only availability is enforced by the official frontend rather than bearer-token security.

**Acceptance Criteria:**
- Given baseline `9ef939552b6b865f42ffe6805fc6b4ac0116bec8`, all matrix scenarios pass and existing desktop delegated-session behavior is unchanged.
- Mobile blocking occurs before `/api/auth/me` can run with a persisted delegated token.
- No backend endpoint, authorization policy, or organization data behavior changes.

## Spec Change Log

- `2026-07-30 / review loop 1` -- Acceptance review found that role-only token checks could restore an expired or unrelated Superadmin token, while malformed active state could survive normalization. Planning now requires actor/home-organization claim equality, unexpired tokens, fail-closed malformed state, and cross-tab-safe cleanup; tests must prove these cases. This avoids cross-Superadmin recovery and sending corrupt/delegated credentials into `/api/auth/me`. KEEP: synchronous pre-auth bootstrap, device-family rather than viewport policy, iPadOS-with-touch detection, stable authenticated blocker, API defense in depth, ordinary mobile access, focused Vitest coverage, and explicit frontend-only security documentation.

## Design Notes

The pre-bootstrap check closes the route-guard gap: `AuthProvider` otherwise reads local storage and `/api/auth/me` can execute before `/superadmin` is mounted. Recovery compares the delegated token's real actor and `homeOrganizationId` with the home token's actor and `organizationId`; the delegated JWT must be structurally readable and the home JWT must also be unexpired. An expired delegated JWT remains recoverable because expiry is a normal reason to restore home. Signature validation remains the API's responsibility. Route, layout, and action guards remain as defense in depth for fresh logins and future call sites.

## Verification

**Commands:**
- `npm test -- --run` from `src/FE` -- focused tests pass.
- `npm run build` from `src/FE` -- TypeScript, service worker, and Vite build pass.
- `npm run lint` from `src/FE` -- no new lint errors.
- `git diff --check` from repository root -- no whitespace errors.

**Manual checks:**
- Emulate desktop Chrome, iPhone/Android, and iPadOS Macintosh-with-touch; verify the matrix and that blocked clients make zero `/api/organizations*` requests.

## Suggested Review Order

**Authentication and tenant safety**

- Normalize persisted delegation before AuthProvider can issue `/api/auth/me`.
  [`main.tsx:10`](../../../src/FE/src/main.tsx#L10)

- Select fail-closed recovery actions and recheck cross-tab storage before mutation.
  [`organizationSession.ts:156`](../../../src/FE/src/features/superadmin/organizationSession.ts#L156)

- Bind recovery to expiry, actor identity, home organization, and customer organization.
  [`organizationSession.ts:239`](../../../src/FE/src/features/superadmin/organizationSession.ts#L239)

- Validate current, home, delegated, and selected-organization state before activation.
  [`organizationSession.ts:71`](../../../src/FE/src/features/superadmin/organizationSession.ts#L71)

**Desktop product boundary**

- Classify iOS, Android, and Macintosh-with-touch iPadOS independently of viewport width.
  [`platform.ts:11`](../../../src/FE/src/lib/platform.ts#L11)

- Guard both authenticated route trees before their layouts mount.
  [`index.tsx:230`](../../../src/FE/src/routes/index.tsx#L230)

- Keep AppLayout independently fail-closed if future routing bypasses the boundary.
  [`AppLayout.tsx:83`](../../../src/FE/src/components/layouts/AppLayout.tsx#L83)

- Present the stable blocker while leaving ordinary mobile users untouched.
  [`DesktopOnlySuperadmin.tsx:41`](../../../src/FE/src/features/superadmin/components/DesktopOnlySuperadmin.tsx#L41)

- Prevent organization queries and actions even when SuperAdmin mounts directly.
  [`SuperAdmin.tsx:44`](../../../src/FE/src/features/superadmin/routes/SuperAdmin.tsx#L44)

- Reject every Superadmin organization request before network access on mobile.
  [`api.ts:16`](../../../src/FE/src/features/superadmin/api.ts#L16)

**Contract and verification**

- Document valid recovery, invalid-login fallback, and frontend-only enforcement.
  [`contract.md:78`](../../api/contract.md#L78)

- Exercise recovery corruption, expiry, cross-tab changes, and activation consistency.
  [`organizationSession.test.ts:84`](../../../src/FE/src/features/superadmin/organizationSession.test.ts#L84)

- Prove mobile organization APIs make zero client calls.
  [`api.test.ts:30`](../../../src/FE/src/features/superadmin/api.test.ts#L30)

- Prove AppLayout blocks mobile Superadmins without relying on router composition.
  [`AppLayout.desktopOnly.test.tsx:21`](../../../src/FE/src/components/layouts/AppLayout.desktopOnly.test.tsx#L21)

- Pin the Node 20-compatible Vitest and jsdom harness.
  [`package.json:47`](../../../src/FE/package.json#L47)
