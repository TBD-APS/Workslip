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
- Users still reauthenticate after the 14-day absolute limit, after explicit logout, after identity removal, or when reuse protection detects compromise.
- The schema migration must be applied before deploying API code that creates refresh sessions.
