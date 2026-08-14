# ADR 0008: Private blob storage for job and profile images

**Status:** Accepted  
**Date:** 2026-08-11

## Context

Workslip needs employee-uploaded images on jobs and employee profile images. These files can contain personal data and can become large enough that storing image binaries in Azure SQL would increase database size, backup cost and coupling without improving the relational model.

The platform already provisions one private Azure Storage account, an `uploads` blob container and managed-identity `Storage Blob Data Contributor` access for the API. Public blob access is disabled.

Image access must continue to respect `OrganizationId` as the tenant/security boundary. Browser clients must not receive storage credentials or durable public/SAS URLs that bypass Workslip authorization.

## Decision

Use the existing private Azure Blob Storage account for production image binaries and keep image authorization in the Workslip API.

The implementation follows these rules:

- Image binaries are not stored in SQL.
- Job image blob names contain only opaque IDs and are scoped as `organizations/{OrganizationId}/jobs/{JobId}/images/{ImageId}`.
- Profile images use one deterministic blob name, `organizations/{OrganizationId}/users/{UserId}/profile`, so replacement overwrites the previous image instead of creating orphaned versions.
- The application validates organization/job/user access before storage operations. Auditor job-image reads additionally use the existing auditor job scope.
- The API accepts JPEG, PNG and WebP up to 10 MB per file and verifies both the declared MIME type and file signature. SVG, HTML and arbitrary binary uploads are rejected.
- Original filenames are not stored in blob names, logs or API responses.
- Image bytes are returned through authenticated API endpoints with `no-store`; the frontend creates temporary browser `blob:` URLs and never receives Azure storage keys or durable public URLs.
- Job-image listing uses the tenant/job blob prefix and Blob Storage properties as image metadata. A new SQL image table is not introduced until a concrete relational query or metadata requirement justifies one.
- Profile images are deleted before user deletion. Job image prefixes are deleted before the existing permanent scheduled job purge. Storage failures block the owning database deletion so cleanup can be retried.
- Azure Storage soft-delete remains the platform recovery layer after application deletion.
- Development uses the same path model on local filesystem storage instead of Azure Blob Storage, preventing local test uploads from reaching a configured Azure environment.

## Consequences

The solution reuses existing infrastructure and managed identity, keeps the relational schema unchanged and preserves tenant authorization at the API boundary.

Listing and displaying many job images does not require database joins, but image reads are authenticated API calls. The frontend therefore lazy-loads image bytes so a job with 25 or more images does not download every full-size image immediately on mobile.

If Workslip later needs searchable captions, image ordering, thumbnails generated server-side, malware-analysis status or richer audit metadata, those are separate requirements that can justify persisted image metadata without moving the binary files out of Blob Storage.
