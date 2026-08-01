---
title: 'WOR-250: Pull-to-refresh for the job list'
type: 'bugfix'
created: '2026-08-01'
status: 'done'
baseline_commit: 'f01f8b7854d3929818aa7ae9e95815f9c2729580'
context:
  - '{project-root}/Docs/agents/VALIDATION.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A job deleted by another user can remain visible in an already-open mobile job list until the existing background polling or focus refresh runs. The backend already invalidates its organization-wide cache, but it cannot directly clear another browser's React Query cache.

**Approach:** Add a mobile pull-to-refresh gesture to the job list. Pulling downward while the app scroll container is already at the top will show progress, and releasing after a clear threshold will refetch the active job-list query so deleted jobs disappear without realtime infrastructure.

## Boundaries & Constraints

**Always:** Use the existing `.app-shell` scroll container and React Query list state; preserve normal vertical scrolling, horizontal sort controls, infinite scrolling, status/search/sort filters, safe areas, and the current 60-second background refresh; show an understandable pull/refresh state; prevent concurrent refresh gestures.

**Ask First:** Expanding pull-to-refresh to routes other than the primary job list, changing polling cadence, or introducing cross-client realtime delivery.

**Never:** Reload the document, bypass React Query, add a backend endpoint, change service-worker caching, add a dependency, or treat the gesture as proof that backend data changed.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|---------------------------|----------------|
| Refresh | Touch begins with `.app-shell.scrollTop === 0`, moves primarily downward past threshold, then releases | Indicator enters refreshing state and the active filtered/sorted job-list pages are fetched again | Existing rows remain visible while fetching; normal query error handling remains available |
| Short pull | Downward pull ends below threshold | Indicator resets without a network request | None |
| Not at top | Gesture starts after the list has been scrolled | Normal scrolling continues; no pull state or refresh | None |
| Horizontal gesture | Gesture is primarily horizontal, such as on mobile sort controls | Existing horizontal interaction continues; no refresh | None |
| Already refreshing | A second gesture occurs while refresh is pending | No duplicate refresh is started | Existing refresh completes normally |

</frozen-after-approval>

## Code Map

- `src/FE/src/features/jobs/routes/JobList.tsx` -- owns the active mobile job-list query and renders list-level loading feedback.
- `src/FE/src/hooks/usePaginatedList.ts` -- exposes the active infinite-query refetch operation.
- `src/FE/src/App.css` -- contains app-shell, job-list, responsive, and motion styling.
- `src/FE/src/hooks/usePullToRefresh.ts` -- new focused touch-gesture lifecycle without a third-party dependency.
- `src/FE/src/hooks/usePullToRefresh.test.tsx` -- regression coverage for threshold, scroll position, direction, and concurrency.

## Tasks & Acceptance

**Execution:**
- [x] `src/FE/src/hooks/usePullToRefresh.ts` -- implement top-of-scroll touch tracking, damped progress, threshold release, and async refresh locking.
- [x] `src/FE/src/features/jobs/routes/JobList.tsx` -- connect the gesture to the existing list refetch and render accessible pull/refresh feedback on mobile.
- [x] `src/FE/src/App.css` -- style the indicator with safe positioning, reduced-motion behavior, and no desktop layout change.
- [x] `src/FE/src/hooks/usePullToRefresh.test.tsx` -- cover all meaningful branches in the edge-case matrix.

**Acceptance Criteria:**
- Given the installed/mobile app is on the job list at its top, when the user pulls down past the threshold and releases, then the current job-list data is fetched again and a remotely deleted job is removed from the rendered list.
- Given the list is scrolled or the gesture is horizontal/too short, when the touch ends, then no refresh request is made and existing scrolling remains usable.
- Given a refresh is already pending, when another pull is attempted, then only one refresh request remains in flight.
- Given a desktop viewport, when the job list renders, then its existing layout and interactions are unchanged.

## Spec Change Log

## Design Notes

The gesture is intentionally local to the job-list route. A reusable hook keeps event lifecycle and passive-listener rules testable, while avoiding a global app-shell behavior that would unexpectedly refetch jobs from unrelated screens.

## Verification

**Commands:**
- `./node_modules/.bin/eslint src/hooks/usePullToRefresh.ts src/hooks/usePullToRefresh.test.tsx src/hooks/usePaginatedList.ts src/features/jobs/routes/JobList.tsx` -- expected: no ESLint errors.
- `npm test -- --run src/hooks/usePullToRefresh.test.tsx` -- expected: edge-case matrix passes.
- `npm run build` -- expected: TypeScript, service-worker typecheck, Vite, and PWA production build pass.

**Manual checks (if no CLI):**
- Run the built PWA in Chromium at desktop and narrow mobile viewports; on mobile, pull at the top and confirm exactly one `/api/jobs` refresh, updated rows, visible feedback, working horizontal sort scrolling, and no console/network errors.

**Recorded result (2026-08-01):**
- Focused Vitest: 1 file, 7 tests passed.
- Targeted ESLint: 0 errors; the existing `usePaginatedList.ts:155` dependency warning remains unchanged.
- Full TypeScript, service-worker, Vite, and inject-manifest PWA production build passed against the repository's current backend contract. The ignored local generated client was reconciled because the deployed production OpenAPI endpoint still lags four existing job-list fields; no generated files are part of WOR-250.
- Built PWA in Chromium at 390x844: one full pull issued exactly one new `/api/jobs` request and changed the rendered list from one job to the empty state.
- Built PWA in Chromium at 1280x900: the existing job table rendered normally and the pull indicator was present in the DOM but not visible.
- The production service worker was requested and active during the browser run. No feature/page errors were observed; the isolated harness produced only the expected denied-notification permission warning. Temporary validation scripts and processes were removed afterward.

## Suggested Review Order

**Job-list integration**

- Start where the existing query refetch becomes the mobile gesture action.
  [`JobList.tsx:123`](../../../src/FE/src/features/jobs/routes/JobList.tsx#L123)

- Review the accessible progress indicator and its three user-visible states.
  [`JobList.tsx:170`](../../../src/FE/src/features/jobs/routes/JobList.tsx#L170)

- Confirm refetch completion is exposed without changing query ownership.
  [`usePaginatedList.ts:226`](../../../src/FE/src/hooks/usePaginatedList.ts#L226)

**Gesture boundary**

- Inspect top-of-scroll, overlay, and single-touch admission rules.
  [`usePullToRefresh.ts:56`](../../../src/FE/src/hooks/usePullToRefresh.ts#L56)

- Inspect direction detection, native-scroll preservation, and damped progress.
  [`usePullToRefresh.ts:79`](../../../src/FE/src/hooks/usePullToRefresh.ts#L79)

- Inspect threshold release, concurrency locking, and failure recovery.
  [`usePullToRefresh.ts:102`](../../../src/FE/src/hooks/usePullToRefresh.ts#L102)

**Presentation and regression protection**

- Confirm the indicator remains mobile-only and respects reduced motion.
  [`App.css:312`](../../../src/FE/src/App.css#L312)

- Finish with threshold, direction, concurrency, multi-touch, and overlay tests.
  [`usePullToRefresh.test.tsx:24`](../../../src/FE/src/hooks/usePullToRefresh.test.tsx#L24)
