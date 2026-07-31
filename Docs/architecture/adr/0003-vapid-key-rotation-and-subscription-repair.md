# ADR 0003: Derive VAPID public keys and repair browser subscriptions

**Status:** Accepted  
**Date:** 2026-07-30  
**Owner:** Workslip architecture owner  
**Linear:** WOR-223

## Context

Workslip delivers in-app notification history from the SQL notification outbox and out-of-app notifications through Web Push. Web Push requires the browser subscription and the server sender to use the same VAPID P-256 key pair.

The previous implementation configured the frontend public key, backend public key and backend private key independently. The browser reused any existing `PushSubscription` without comparing its `applicationServerKey` with the currently configured key. Rotating the private key could therefore leave both an unmatched server key pair and browser subscriptions permanently tied to the previous key. In-app history could continue working while every external delivery failed.

## Decision

1. `Vapid:PrivateKey` is the authoritative VAPID key material.
2. The backend derives the uncompressed P-256 public key from that private key at runtime.
3. `Vapid:PublicKey` is treated only as a migration diagnostic. A mismatch is logged without exposing either key, and the derived public key is used for sending.
4. The authenticated push-subscription API exposes the active derived public key. The frontend no longer depends on a separately managed `VITE_VAPID_PUBLIC_KEY` value.
5. During authenticated startup, the frontend compares the existing browser subscription's `applicationServerKey` with the active server key.
6. A stale subscription is unsubscribed and recreated with the active key. The replacement request includes the old endpoint so the backend can deactivate exactly that database row without disabling other devices.
7. Push delivery continues to use the notification outbox and existing retry worker. Key rotation repair occurs before future notifications are queued for that browser.

## Consequences

- A private-key rotation automatically produces the matching public key used by both sender and browser.
- Existing installed PWAs repair their subscription on the next authenticated startup without asking the user to re-enable notification permission.
- Independent devices remain active because only the explicitly replaced endpoint is disabled.
- A missing or malformed private key produces an actionable configuration failure instead of silent use of an invalid key pair.
- The first successful login after deployment must reach the API and the browser push service before that device is repaired.
- Real out-of-app delivery still requires a deployed smoke test because browser push providers and the production secret cannot be proven by unit tests alone.

## Rejected alternatives

- Keep separate public-key values in Azure and Vercel: rejected because they can drift during rotation.
- Reuse every existing browser subscription: rejected because subscriptions are bound to their original application server key.
- Disable every other subscription for the user during repair: rejected because it would remove legitimate additional devices.
- Treat HTTP 401/403 push responses as expired subscriptions: rejected because those responses may represent server key configuration failure rather than an invalid endpoint.
