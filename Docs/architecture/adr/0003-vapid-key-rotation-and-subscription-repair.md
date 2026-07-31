# ADR 0003: Derive VAPID public keys and repair browser subscriptions

**Status:** Accepted  
**Date:** 2026-07-30  
**Owner:** Workslip architecture owner  
**Linear:** WOR-223

## Context

Workslip delivers in-app notification history from the SQL notification outbox and out-of-app notifications through Web Push. Web Push requires the browser subscription and server sender to use the same VAPID P-256 key pair.

The previous implementation configured public and private key material independently. The exposed private key was removed from committed infrastructure, but the supported deployment path did not create its secure replacement. Production could therefore continue writing in-app notifications while all Web Push delivery failed.

## Decision

1. `Vapid:PrivateKey` is the authoritative VAPID credential.
2. The backend derives the matching public key at runtime and exposes it through the authenticated push-subscription API.
3. The frontend compares the existing browser subscription with the active derived key and replaces stale subscriptions.
4. Full infrastructure deployment preserves an enabled Key Vault secret named `Vapid--PrivateKey` and generates a valid P-256 private scalar only when that secret is missing or disabled.
5. Deployment creates the versionless App Configuration Key Vault reference `Vapid:PrivateKey` and restarts the API.
6. Deployment does not read, update or delete separately configured `Vapid:PublicKey` state. The historical Bicep declaration is removed, and any existing Azure value is an operator cleanup action.
7. A push-provider response identifying `VapidPkHashMismatch` is a permanent failure for that specific stored endpoint. The worker deactivates only that subscription and does not retry the notification solely because of that endpoint. Other valid device subscriptions remain active and continue receiving notifications.

## Consequences

- Missing private key material is repaired by the normal full deployment.
- Ordinary deployments preserve the existing key and do not invalidate browser subscriptions.
- A newly generated key requires each installed PWA to open once so its browser subscription can be repaired.
- Stale subscriptions that have not yet reopened are removed lazily when the push provider reports a VAPID public-key mismatch, preventing repeated delivery attempts against an endpoint that cannot accept the active key.
- Private key material is never committed or printed by deployment.
- Real OS-level delivery still requires a deployed smoke test.

## Rejected alternatives

- Generate a new VAPID key on every deployment: rejected because every browser subscription would become stale on every release.
- Store the private key in Bicep or source control: rejected because it is a credential.
- Keep independently managed frontend and backend public-key settings: rejected because they can drift from the private key.
- Disable all subscriptions during repair: rejected because users may have legitimate subscriptions on several devices.
