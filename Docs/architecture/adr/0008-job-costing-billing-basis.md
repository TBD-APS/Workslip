# ADR 0008: Job costing keeps pricing separate from identity and snapshots finalized rates

**Status:** Accepted  
**Date:** 2026-08-14

## Context

Workslip needs an administrator-visible billing basis for registered employee hours. The MVP requires a current billable hourly rate per employee, reproducible DKK line amounts in the monthly Admin PDF, and historical integrity when a rate changes after a job has been approved.

The existing `Users` model is also used by authentication, delegated Superadmin sessions, onboarding and platform identity bootstrap. Putting commercial pricing directly on that persistence model would couple job costing to identity flows and broaden the surface where economic data can accidentally be read or written.

Approved jobs are terminal in the current job-status policy. Their registered hours therefore provide a natural finalization boundary for preserving the commercial basis that applied at approval time.

## Decision

Job costing is a separate application and persistence concern from authentication/user profile data.

The implementation follows these rules:

- `OrganizationId` remains the tenant/security boundary for all pricing reads and writes.
- Current employee pricing is stored in `UserBillingRates`, keyed by `(OrganizationId, UserId)`, not on `Users`.
- Finalized pricing is stored per worksheet in `WorksheetBillingSnapshots`, keyed by `(OrganizationId, WorksheetId)`.
- A successful transition to `Approved` captures the current employee rate for every worksheet on the job. A snapshot row is also written when the applicable rate is `NULL`, so "no configured rate" is historical state rather than permission to use a future rate.
- Rate changes run inside a serializable transaction. Before the new current rate is written, any already-Approved worksheets that still lack a snapshot are preserved with the previous rate. This closes the approval/rate-change race without allowing later prices to rewrite finalized history.
- Approved worksheet rows are immutable through the EF write pipeline. Hours cannot be inserted, changed or deleted after finalization.
- Admin report reads use the current rate for non-finalized work and the snapshot only for `Approved` work. An Approved row with a missing/null snapshot never falls back to a later current rate.
- Line amounts use `decimal` and are rounded at line level to two decimals with `MidpointRounding.AwayFromZero`; report totals sum the rounded line amounts.
- DKK is the only currency in this MVP. VAT, cost price, margin, invoice/accounting integration and multi-currency are outside this decision.
- Pricing management is exposed through the Admin-only `/api/job-costing` boundary. Shared auth/profile contracts remain pricing-free, and employee `/api/worksheets/my` does not populate pricing values.
- Missing rates are shown explicitly in the Admin PDF and excluded from monetary totals rather than silently treated as zero.
- Rate values are not written to application logs or telemetry.

## Consequences

Authentication and platform identity flows remain independent of commercial pricing, reducing accidental disclosure and preventing job-costing schema changes from forcing unrelated auth fixtures or bootstrap paths to change.

Finalized job economics are reproducible after current rates change. If snapshot capture is incomplete, Workslip fails closed for historical pricing: the Admin report shows a missing basis instead of substituting the current rate.

The design adds two dedicated tables and a small job-repository decorator/finalization guard rather than turning the existing user model or worksheet model into general accounting entities.

The Admin monthly worksheet response carries nullable costing values for the administrative report path; the employee path leaves them unset. A future dedicated costing report contract can replace this shared shape if the product grows beyond this MVP.
