# ADR 0018 — Rotating server-owned refresh sessions

**Status:** Accepted  
**Owner:** Workslip maintainers  
**Issue:** WOR-467

## Context

Workslip access JWTs expire after a short, bounded lifetime. The browser previously handled expiry by starting Microsoft Entra authentication again, so an otherwise valid user could be asked to sign in whenever the application was reopened after the access token expired. Extending the bearer-token lifetime would reduce prompts but would also extend the useful lifetime of a stolen token and would not provide server-side logout.

## Decision

1. Access JWTs remain short lived and are held by the existing browser session code.
2. A successful OTC, Entra login or Entra enrollment also creates a server-owned refresh-session family with an absolute lifetime of 14 days.
3. The browser receives the opaque refresh token only as an `HttpOnly`, `SameSite=Strict`, `Secure` production cookie. The database stores only its SHA-256 hash.
4. `POST /api/auth/refresh` rotates the refresh token exactly once and returns a new access JWT. The absolute family expiry is not extended by rotation.
5. Reuse revokes the whole family. Reuse inside a 30-second concurrent-request window returns `409 refresh_race` without revocation so simultaneous tabs can retry with the already-rotated cookie. Late reuse revokes the family.
6. Every refresh verifies that the exact user and organization still exist. The current identity schema has no independent active/disabled flag, so existence is the current eligibility contract.
7. `POST /api/auth/logout` revokes the family before clearing the cookie. Expired/revoked rows are deleted by a background cleanup worker after the retention window.
8. Refresh and logout require an exact configured frontend `Origin`, use credentialed CORS, never return refresh material in JSON, and never log tokens or token hashes.

## Consequences

- Normal access-token expiry is recovered without an interactive Entra redirect.
- Administrators can invalidate a browser session through server-side family revocation.
- Users still reauthenticate after 14 idle days or the 90-day absolute limit (see the WOR-813 amendment), after explicit logout, after identity removal, or when reuse protection detects compromise.
- The schema migration must be applied before deploying API code that creates refresh sessions.

## Amendment — WOR-813 (2026-10-15)

Live logs from 2026-09-24 to 2026-10-15 showed users being signed out although they were active. There were two causes. The family expired 14 days after login, however often it was refreshed. And a refresh whose response never reached the browser (a sleeping laptop or a lost mobile connection) looked like token reuse, so the whole family was revoked. Points 2, 4 and 5 are amended:

2. A family has an **absolute** lifetime of 90 days, stored as `ExpiresAt` on every row of the family. A refresh token also expires when it has been **idle** for 14 days, counted from its own `CreatedAt`. The cookie expires at the earlier of the two, so each successful refresh moves the cookie's expiry forward.
4. Rotation remains one-use and does not extend the absolute expiry; it moves the idle expiry forward.
5. Reuse inside the 30-second window still returns `409 refresh_race`. Reuse after the window is classified:
   - If the presented token is the most recently used one and its direct replacement (the family row created at the token's `UsedAt`) has never been used or revoked, the browser lost the response. The unused replacement is revoked, the presented token is stamped as used again, and a new replacement is issued. The session continues.
   - Otherwise (the replacement was already used, or an older token is replayed) the whole family is revoked, as before.
   - A revoked token that is presented again revokes its whole family. This is how a superseded replacement, held by a second party, is detected after a lost-response recovery.

Rejected refreshes are logged with the reason (no cookie, unknown token, revoked, absolute or idle expiry, reuse) but never with token material.

The browser treats only a `401` or `403` from `POST /api/auth/refresh` as the end of the session. A network error, a server error or rate limiting keeps the stored login and shows a recoverable error, so the next request can refresh again.

Sessions created before this amendment keep their stored 14-day `ExpiresAt` as their absolute limit. No schema change or data migration is required.
