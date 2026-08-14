# Security notes

The AI review workflow is intentionally split into model jobs and a posting job.

- Model jobs receive model-provider credentials but no write-capable GitHub credential.
- The posting job receives `WORKSLIP_REVIEW_PAT` but does not invoke either model.
- Contributor-controlled code is never checked out in a job that has secrets.
- Contributor-controlled PR text and diffs are treated as untrusted data and redacted before review.
- Third-party Actions are pinned to commit SHAs.
- No review path can approve, merge, push, modify repository files, deploy or access production systems.

If a future change weakens any of these boundaries, treat that change as a security-sensitive workflow change and require explicit review before merge.
