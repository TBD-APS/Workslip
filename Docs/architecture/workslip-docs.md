# Workslip Docs boundary

**Status:** Active  
**Owner:** Product + backend/frontend maintainers  
**Linear:** WOR-455  
**Review cadence:** When Docs authorization, persistence, retention, sharing or external integrations change

## Purpose

Workslip Docs is the tenant-scoped in-product knowledge base for company and operational knowledge that is expensive to rediscover: internal procedures, onboarding notes, product rationale, UX guidance and reusable team knowledge.

It does **not** replace repository documentation. Current code/configuration/tests and maintained repository `Docs/` remain authoritative for implementation behaviour, API contracts, architecture, operations and compliance. Product documents should link to those sources instead of copying technical truth that can drift.

## V1 trust boundary

- Read access uses the existing organization read-access policy (`User|Auditor`, with the configured role hierarchy giving Admin/Superadmin the inherited access).
- Create, update and delete use the existing Admin policy.
- Every persistence query includes `OrganizationId`; document IDs are never sufficient to read or mutate a document.
- Read and mutation responses use `Cache-Control: no-store` through the existing HTTP cache helper.
- Document title/content/tags are not written to application logs or telemetry by the feature.
- There is no public sharing, external processor, AI/RAG integration or attachment storage in v1.
- Updates use an incrementing revision and fail on stale writes instead of overwriting concurrent edits.

## Persistence

`dbo.KnowledgeDocuments` stores:

- tenant ownership (`OrganizationId`);
- title, plain-text content and a bounded JSON tag list;
- optional creator/updater user references used only to resolve current display context;
- created/updated timestamps;
- an optimistic-concurrency revision.

The actor IDs intentionally are not foreign keys to `Users`: deleting or moving a user must not prevent document lifecycle operations, and display names are not snapshotted into document rows. If an actor no longer exists in the tenant, the UI simply has no display name to show.

The v1 editor stores plain text. This is deliberate: a bespoke HTML/Markdown renderer would expand the XSS and sanitization surface without being necessary for the first complete knowledge-base slice.

## Privacy and retention gate

Document free text may contain personal or confidential business information. The technical implementation therefore follows the GDPR baseline controls above, but engineering does not choose the legal basis or retention rule.

Production merge/release of WOR-455 remains blocked until the accountable product/compliance owner records the processing purpose/legal roles, lawful-basis owner or customer instruction, retention/deletion handling (including tenant termination), and DPIA screening result. No claim of GDPR compliance follows from this implementation alone.

## Future extensions

Attachments, version history, comments/mentions, rich collaborative editing, public/external sharing and AI-assisted search are separate product/security decisions. They must not be inferred from the v1 table or API contract.
