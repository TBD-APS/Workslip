---
title: 'Resolve RBJ-107 branch conflicts'
type: 'chore'
created: '2026-07-30'
status: 'done'
route: 'one-shot'
---

# Resolve RBJ-107 branch conflicts

## Intent

**Problem:** The local RBJ-107 branch had diverged from its force-updated remote branch and conflicted with the latest authentication recovery changes on `main`.

**Approach:** Reconcile the divergent histories without discarding recoverable commits, merge the latest `main`, and combine delegated-session recovery with normal reauthentication. Harden the combined interceptor against concurrent stale 401 responses.

## Suggested Review Order

**401 recovery boundary**

- Distinguishes stale responses, delegated expiry, and ordinary reauthentication.
  [`axios.ts:178`](../../../src/FE/src/lib/axios.ts#L178)

- Extracts the original request token for concurrency-safe response handling.
  [`axios.ts:49`](../../../src/FE/src/lib/axios.ts#L49)

**Delegated-session validation**

- Requires an explicit delegated-session JWT claim before restoring home credentials.
  [`organizationSession.ts:41`](../../../src/FE/src/features/superadmin/organizationSession.ts#L41)

- Rejects incomplete persisted session metadata during restoration.
  [`organizationSession.ts:84`](../../../src/FE/src/features/superadmin/organizationSession.ts#L84)

**Mainline authentication recovery**

- Consolidates failed reauthentication cleanup while preserving RBJ-209 behavior.
  [`Login.tsx:58`](../../../src/FE/src/features/auth/routes/Login.tsx#L58)
