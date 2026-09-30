# Auditor v1

**Linear:** WOR-814

Auditor v1 gives an external VVS auditor read access to explicitly assigned organizations and lets the auditor create and close separate audit findings without changing the company's normal job lifecycle.

## Access model

- Auditor access is server-authoritative and fail-closed.
- An active `AuditorAssignment` links the auditor user and control organization to one target organization and authorization area.
- Auditor cross-tenant reads use dedicated `/api/auditor` endpoints. Ordinary tenant job APIs are not widened for auditor use.
- A report is visible only when the assignment is active, the report belongs to the assigned target organization, the report is approved, `IsInAuditorScope` is enabled, and the report contains a `Vand` or `Afløb` installation.
- Assignment validity is checked on every auditor request; frontend selection is never treated as authorization.

## Audit findings

Findings are stored separately from the normal job status. Supported categories are:

- `A` — Afvigelse
- `An` — Anmærkning
- `Anb` — Anbefaling
- `IR` — Ikke relevant

The finding lifecycle is `Open` → `AwaitingEvidence` → `ReadyForVerification` → `Closed`. Company evidence and auditor verification are recorded independently from job approval/rejection.

## Traceability and privacy

Auditor actions are written to the append-only auditor event trail. The auditor response shape is minimized for the KLS review: customer email/phone/contact person, employee email and raw event JSON are not serialized to the auditor client. Images are fetched through authenticated auditor endpoints and the existing image storage boundary.

## Rollback

The database migration is expand-only. Application rollback can leave the auditor tables unused; schema removal, if ever required, must be handled by a separate reviewed migration. Do not deploy Auditor v1 unless tenant/authorization tests, frontend validation, browser evidence and the repository's required CI gates are green on the exact release head.
