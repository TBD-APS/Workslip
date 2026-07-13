# Design: Map App Fallback + Device Detection at Login

**Date:** 2026-07-13  
**Status:** Approved  
**Owner:** Rasmus

---

## Problem

1. The Navigation icon in `DestinationAddressBlock` always opens Google Maps — on mobile, if Google Maps isn't installed, the URL opens in the browser (poor UX) or fails silently.
2. No record of which device/browser users log in from — makes debugging support issues harder.

---

## Goals

- Try native map app first on mobile; show fallback dialog only when it fails.
- Store full `navigator.userAgent` string on each login in `UserDataRow.LastUserAgent`.

---

## Non-Goals

- Storing multiple devices per user (future consideration).
- Map app detection / installation checks (not reliably possible from web).
- Offline map support.

---

## Design

### 1. Platform Detection Utility

**New file:** `src/FE/src/lib/platform.ts`

```ts
export type Platform = 'ios' | 'android' | 'desktop';

export function detectPlatform(): Platform { ... }
export function isMobile(): boolean { ... }
```

Parses `navigator.userAgent`. No dependencies.

### 2. Map App Fallback (Mobile)

**Modified file:** `src/FE/src/features/jobs/components/JobDetailBlocks.tsx`

Current: `<a href="https://maps.google.com/...">` always opens Google Maps in new tab.

New behavior on click:
- **Desktop** (`!isMobile()`): open Google Maps in new tab — unchanged.
- **Mobile**:
  1. Build native intent URL: `geo:0,0?q={encodedAddress}` (works on both iOS and Android — Android routes to Google Maps, iOS shows app chooser).
  2. Set a 2-second timeout.
  3. Attempt `window.location.href = intentUrl`.
  4. If timeout fires (no app handled the intent), show `MapFallbackSheet`.

**New component:** `MapFallbackSheet` — simple modal/bottom sheet with options:
- **Google Maps (browser)** — opens `https://maps.google.com/?q=...` in new tab
- **Copy address** — copies formatted address to clipboard via `navigator.clipboard.writeText`
- **Close** — dismisses sheet

No new dependencies needed. Uses existing UI patterns (modal backdrop).

### 3. Device Detection at Login

**Backend — `UserDataRow`:**
- New nullable column: `string? LastUserAgent`
- Max length: 500 (same as `PushSubscriptionRow.UserAgent`)
- EF migration to add column

**Backend — `AuthContracts.cs`:**
- `VerifyCodeRequest` gains `string? UserAgent`
- `DevTokenRequest` gains `string? UserAgent`

**Backend — `AuthService`:**
- On successful login (after password/code verified), write `user.LastUserAgent = request.UserAgent` before saving.

**Frontend — `Login.tsx`:**
- Read `navigator.userAgent` once at login call time.
- Send `{ email, code, userAgent }` in request body.

**Frontend — `InviteAccept.tsx`:**
- Same — send `navigator.userAgent` in `EntraEnrollRequest` body (field name: `userAgent`).

**Frontend — `AuthContext.tsx`:**
- `login()` and `devLogin()` accept optional third param `userAgent?: string` and include it in the API call.

### 4. Data Flow

```
User taps Login
  → Login.tsx reads navigator.userAgent
  → POST /api/auth/verify-code { email, code, userAgent }
  → AuthService writes user.LastUserAgent = request.UserAgent
  → Returns JWT

User taps Navigation icon
  → isMobile()?
    → No: window.open(mapsUrl) — desktop, unchanged
    → Yes: window.location = geo: intent → 2s timeout → MapFallbackSheet
```

---

## Files Changed

| File | Change |
|------|--------|
| `src/FE/src/lib/platform.ts` | **New** — platform detection utility |
| `src/FE/src/features/jobs/components/JobDetailBlocks.tsx` | Modify `getMapsUrl` usage, add `MapFallbackSheet`, mobile fallback logic |
| `src/BE/WorkslipApi/Workslip.Domain/Models/UserDataRow.cs` | Add `LastUserAgent` property |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs` | Add column config (MaxLength 500, nullable) |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Migrations/` | New migration |
| `src/BE/WorkslipApi/Workslip.Application/Auth/AuthContracts.cs` | Add `UserAgent` to requests |
| `src/BE/WorkslipApi/Workslip.Application/Auth/AuthService.cs` | Write `LastUserAgent` on login |
| `src/FE/src/features/auth/routes/Login.tsx` | Send userAgent in login call |
| `src/FE/src/features/auth/routes/InviteAccept.tsx` | Send userAgent in enrollment call |
| `src/FE/src/providers/authContextValue.ts` | Update `login`/`devLogin` signatures |

---

## Testing

- Unit: `detectPlatform()` with various user-agent strings (iOS Safari, Android Chrome, Desktop Chrome).
- Manual: tap Navigation icon on iOS/Android — should attempt intent, show fallback sheet after 2s.
- Manual: login on desktop — verify `LastUserAgent` populated in DB.
- Manual: login on mobile — verify `LastUserAgent` populated in DB.
