# ADR 0003: Use one VAPID private key and derive the public key

**Status:** Accepted  
**Date:** 2026-07-30  
**Amended:** 2026-07-31  
**Owner:** Workslip architecture owner  
**Linear:** WOR-223

## Context

Workslip delivers out-of-app notifications through Web Push. The browser subscription and server sender must use the same VAPID P-256 key pair, and ordinary deployments must not rotate that pair unintentionally.

A browser can retain more than one persisted subscription row over time. After a VAPID rotation, a repaired current endpoint can therefore coexist with an older active endpoint whose push-provider association still references the previous public key. The provider reports that endpoint with HTTP 400 and JSON reason `VapidPkHashMismatch`. Treating that response as a temporary delivery error leaves the invalid row active, retries the notification repeatedly, and can resend the same notification to other valid device subscriptions.

The first notification repair also retained a blanket Superadmin exclusion based on delegated organization access. That exclusion does not match the persistence model: push subscriptions, queued notifications, history and delivery lookup are keyed by the authenticated actor's `UserId`, while delegated sessions preserve that actor ID and change only the effective `organizationId` claim. Excluding the role therefore disabled legitimate actor-scoped notifications without preventing a tenant-wide feed, because no such feed is created by subscription registration.

## Decision

1. `Vapid:PrivateKey` is the only configured VAPID key.
2. The backend validates it as a 32-byte P-256 private scalar and derives the matching uncompressed public key at runtime.
3. The authenticated push-subscription API exposes the derived public key with `Cache-Control: no-store`.
4. During authenticated startup, the frontend compares the browser subscription's `applicationServerKey` with the active public key.
5. A subscription created with a different key is replaced. Registration includes the replaced endpoint so only that database row is deactivated and subscriptions on other devices are preserved.
6. Full infrastructure deployment preserves the enabled Key Vault secret `Vapid--PrivateKey`. It generates a key only when the secret is missing or disabled, creates the versionless App Configuration reference `Vapid:PrivateKey`, and restarts the API.
7. The custom service worker is enabled for both production builds and local Vite development so the same push-registration and notification-display path can be exercised locally.
8. Every authenticated actor with a valid `UserId`, including Superadmins, may register a device. The subscription remains actor-scoped; entering a delegated organization session does not broaden notification targeting or convert the device into a tenant-wide feed.
9. Delivery deactivates only the subscription that receives HTTP 404, HTTP 410, or HTTP 400 with the exact JSON reason `VapidPkHashMismatch`. Other HTTP 400 responses remain retryable and cannot delete a subscription merely because their human-readable message contains similar text.

## Consequences

- Public-key configuration cannot drift from the private key.
- Ordinary deployments preserve existing browser subscriptions.
- An intentional private-key rotation repairs each browser subscription on its next authenticated startup.
- Persisted stale endpoints are removed incrementally when the push provider identifies them, without clearing unrelated SQL tables or disabling other devices.
- A VAPID-mismatched endpoint does not keep the notification in the temporary retry loop or cause duplicate retries to valid endpoints.
- Multiple device subscriptions remain independent.
- Private key material is not committed, printed, or sent to the frontend.
- Superadmin devices can receive notifications addressed to that Superadmin user ID.
- Delegated organization selection does not broaden delivery because the real actor ID remains unchanged.
- External browser and push-provider delivery still requires a real runtime smoke test.

## Rejected alternatives

- Configure public and private VAPID keys independently: rejected because separate values can drift.
- Generate a new private key on every deployment: rejected because it would invalidate every browser subscription on every release.
- Store the private key in Bicep or source control: rejected because it is a credential.
- Disable every subscription for a user during replacement: rejected because users may have legitimate subscriptions on several devices.
- Delete all push-subscription or notification tables after a rotation: rejected because exact endpoint cleanup is sufficient and preserves valid devices and notification history.
- Match `VapidPkHashMismatch` against the exception message: rejected because provider messages are not a stable machine contract and can contain misleading text.
- Exclude every Superadmin from push registration: rejected because notification ownership is actor-scoped and the exclusion silently disables legitimate notifications.
