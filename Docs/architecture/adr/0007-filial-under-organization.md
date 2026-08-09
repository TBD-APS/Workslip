# ADR 0007: Filial is an organizational scope under Organization

**Status:** Accepted  
**Date:** 2026-08-09

## Context

Workslip needs to support organizations with more than one operational location without weakening the existing multi-tenant model or forcing current single-location customers through new UI/API steps.

`OrganizationId` is already the server-owned tenant/security boundary throughout the application. Replacing it with a lower-level location identifier would expand the authorization surface and make existing data ownership harder to reason about.

The terminology is also a deliberate domain decision: Workslip calls this concept **Filial**, not Branch.

## Decision

Model Filial as a child entity of Organization:

```text
Organization
  └── Filial
       ├── Users
       └── Jobs
```

The implementation follows these rules:

- `OrganizationId` remains the tenant/security boundary.
- `FilialId` is organizational scope below the tenant, never an authorization substitute for `OrganizationId`.
- Every Organization has exactly one default Filial when created/backfilled.
- `Users` and `JobReports` belong to one Filial in the current model.
- Existing single-filial create flows resolve the default Filial server-side; clients are not required to send `FilialId`.
- Customers and installation/reference data remain Organization-level until a concrete requirement justifies narrower ownership.
- Relations that must stay inside one tenant are database-constrained with composite `(OrganizationId, ...)` keys/foreign keys.
- Workslip domain code, schema, API, tests and maintained documentation use `Filial`/`FilialId`; `Branch` is reserved for unrelated technical terminology such as Git branches.

The default Filial uses the Organization ID as its own ID. This makes migration/backfill deterministic and retry-safe without constraining IDs for additional Filials created later.

## Consequences

Single-filial customers keep the current interaction model while the database is ready for later multi-filial administration, selection and filtering.

Authorization continues to reason about one tenant key (`OrganizationId`). Filial validation is an additional same-tenant invariant rather than a second security boundary.

Adding multi-filial UI does not require another tenant migration, but it still requires explicit product work for Filial administration and relevant list/create filtering.

The schema intentionally does not generalize this into a tenant framework or generic hierarchical ownership system. New entities receive Filial ownership only when a concrete product rule requires it.
