---
title: 'Fix SuperAdmin production organization routes'
type: 'bugfix'
created: '2026-07-31'
status: 'done'
route: 'one-shot'
---

# Fix SuperAdmin production organization routes

## Intent

**Problem:** Vercel serves the SPA document for `/api/organizations/`, causing the production SuperAdmin page to treat HTML as organization data and crash during sorting.

**Approach:** Use the slash-free organization collection route for listing and creation, and lock the exact request paths with focused regression tests.

## Suggested Review Order

**Production routing**

- One shared slash-free path keeps list and create requests aligned with Vercel.
  [`api.ts:14`](../../../src/FE/src/features/superadmin/api.ts#L14)

**Regression coverage**

- Desktop tests enforce one exact GET and POST plus the onboarding response contract.
  [`api.test.ts:62`](../../../src/FE/src/features/superadmin/api.test.ts#L62)
