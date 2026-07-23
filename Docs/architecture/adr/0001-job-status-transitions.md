# ADR 0001: Enforce an explicit job status transition matrix

**Status:** Proposed  
**Date:** 2026-07-23  
**Owners:** Product owner and Workslip engineering  
**Related:** WOR-142

## Context

The persisted job statuses are `Draft`, `InReview`, `Approved` and `Rejected`.

The current API checks only that the target value is a valid enum. It does not check the current status before writing the target. Submit-readiness validation runs before every status change, and the repository applies the target in a serializable transaction. A request for the already-current target is treated as an idempotent no-op.

This means the implementation does not currently express which business transitions are legal. Documentation and UI assumptions can therefore drift from server behaviour.

## Proposed decision

Enforce transitions in the application layer before the repository write. The server remains authoritative; frontend controls are only user guidance.

Recommended initial matrix for product approval:

| From | Allowed target | Meaning |
|---|---|---|
| `Draft` | `InReview` | Submit completed job for review. |
| `InReview` | `Approved` | Approver accepts the job. |
| `InReview` | `Rejected` | Approver returns/rejects the job. The exact product wording must be decided. |
| `Rejected` | `InReview` | Corrected job is resubmitted. |
| `Approved` | none | Final state unless a separately authorized reopen/correction command is introduced. |

A same-state request remains a successful no-op for safe retry behaviour.

This matrix is **not implemented by this ADR**. The meaning of `Rejected` and whether reopening an approved job is required need explicit product approval.

## Authorization boundary

- `User`-level access may submit or resubmit when the user is allowed to edit the job.
- Approval and rejection require an explicit reviewer permission or role; relying only on generic `RequireUser` is insufficient.
- Tenant ownership is always derived from authenticated server context.
- A future reopen operation must be a separate, audited command with a reason.

## Consequences

### Positive

- Illegal state jumps are rejected by the server.
- UI, API and documentation can share one transition contract.
- Notification and inventory side effects can be attached to defined transitions.
- Tests can cover a finite matrix.

### Negative

- Existing clients that currently rely on unrestricted target changes may fail.
- Product ownership must settle the meaning of rejection/correction.
- A migration or cleanup may be needed if existing records reflect impossible historical sequences.

## Alternatives considered

### Continue accepting any enum target

Rejected as a target design because it makes workflow rules implicit and allows accidental state jumps.

### Put the matrix only in the frontend

Rejected because the frontend is not a security or integrity boundary.

### Model every action as a new status

Not selected initially. Commands such as reopen/cancel may be better represented as explicit audited actions rather than expanding the core status enum prematurely.

## Verification required before acceptance

- Product approval of each transition and role.
- Application-layer transition tests for every allowed and denied pair.
- Endpoint tests for permission, tenant isolation and idempotent same-state retry.
- Notification tests proving side effects occur once.
- Updated UI actions and user guide using the same wording.
