---
title: 'Fix active merge resolution'
type: 'bugfix'
created: '2026-07-30'
status: 'done'
route: 'one-shot'
---

# Fix active merge resolution

## Intent

**Problem:** The main-branch merge had been staged with unresolved conflict markers in the endpoint catalog, despite Git reporting that all conflicts were fixed.

**Approach:** Select the newer contract-review date, preserve both branches' substantive endpoint documentation, remove every conflict marker, and verify the combined frontend and backend tree before accepting the merge.

## Suggested Review Order

- Confirm the catalog preserves the resolved review date and both endpoint changes.
  [`endpoint-catalog.md:3`](../../api/endpoint-catalog.md#L3)

- Confirm desktop-only Superadmin behavior survives the merged layout changes.
  [`AppLayout.tsx:97`](../../../src/FE/src/components/layouts/AppLayout.tsx#L97)

- Confirm Vitest and prompted PWA-update configuration coexist after merging.
  [`vite.config.ts:8`](../../../src/FE/vite.config.ts#L8)
