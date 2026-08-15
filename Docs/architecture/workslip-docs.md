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
- Create, update, delete and attachment upload/delete use the existing Admin policy.
- Every document and attachment persistence query includes `OrganizationId`; document/attachment IDs are never sufficient to read or mutate data.
- Attachment metadata has a composite `(OrganizationId, DocumentId)` foreign key to the owning document, preventing cross-tenant references at the database layer as well as in repository predicates.
- Read and mutation responses use `Cache-Control: no-store` through the existing HTTP cache helper.
- Document content, tags and attachment file names are not written to application logs or telemetry by the feature.
- There is no public sharing, external processor or AI/RAG integration in v1.
- Updates use an incrementing revision and fail on stale writes instead of overwriting concurrent edits.

## Persistence

`dbo.KnowledgeDocuments` stores:

- tenant ownership (`OrganizationId`);
- title, plain-text content and a bounded JSON tag list;
- optional creator/updater user references used only to resolve current display context;
- created/updated timestamps;
- an optimistic-concurrency revision.

`dbo.KnowledgeDocumentAttachments` stores tenant-scoped metadata only: document ownership, file name, canonical content type, byte size, uploader context and creation time. File bytes are stored behind the existing private `Azure:DocumentFileStorage` boundary in production and the established local file root during development. Blob/local paths include organization, document and attachment IDs; no public blob URL is exposed to clients.

The v1 attachment allow-list is intentionally narrow: MP3/WAV/OGG audio, MP4, PDF, PNG/JPEG/WebP, TXT/Markdown and CSV, with a 20 MB per-file limit. HTML, SVG and executable/arbitrary file types are not accepted. Audio is downloaded through the authenticated API and played from a browser object URL rather than exposing storage directly.

The actor IDs intentionally are not foreign keys to `Users`: deleting or moving a user must not prevent document lifecycle operations, and display names are not snapshotted into document rows. If an actor no longer exists in the tenant, the UI simply has no display name to show.

The v1 editor stores plain text. This is deliberate: a bespoke HTML/Markdown renderer would expand the XSS and sanitization surface without being necessary for the first complete knowledge-base slice.

## Delete and partial-failure semantics

Document deletion is authoritative in SQL and cascades attachment metadata. The service then deletes the document's attachment prefix from private storage. If storage cleanup fails after the SQL delete, the objects are unreachable through Workslip; the IDs are logged for operational orphan cleanup rather than re-creating deleted metadata.

Attachment upload writes the private object first and then metadata. If the metadata write fails, the service attempts immediate object cleanup before surfacing the failure. Attachment deletion removes metadata first and then the private object; a storage-cleanup failure is logged as an unreachable orphan.

## Privacy and retention gate

Document free text and attachments may contain personal or confidential business information. The technical implementation therefore follows the GDPR baseline controls above, but engineering does not choose the legal basis or retention rule.

Production merge/release of WOR-455 remains blocked until the accountable product/compliance owner records the processing purpose/legal roles, lawful-basis owner or customer instruction, retention/deletion handling (including tenant termination), and DPIA screening result. No claim of GDPR compliance follows from this implementation alone.

## Future extensions

Version history, comments/mentions, rich collaborative editing, public/external sharing and AI-assisted search are separate product/security decisions. They must not be inferred from the v1 table or API contract.
