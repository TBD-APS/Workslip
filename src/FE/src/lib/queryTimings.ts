// Cache-timing constants for the React Query layer.
//
// Two layers of freshness live here:
//
// 1. `DEFAULT_*` — applied to every query in the app unless a query family
//    overrides them via `setQueryDefaults` or a hook passes its own options.
//    Keep these conservative; per-feature overrides are cheaper than raising
//    the global cost.
//
// 2. Feature families such as `JOB_LIST_*` and `NOTIFICATION_LIST_*` — applied
//    only to server state that must refresh while the authenticated app remains
//    open. New families should follow the `setQueryDefaults` pattern in
//    `createQueryClient.ts` rather than implementing component-local polling.
//
// When adding a new constant: pick a name that describes a behaviour, not a
// consumer (e.g. `DEFAULT_QUERY_RETRY` not `AUTH_RETRY`). Behaviour-named
// constants stay correct when the consumer is renamed; consumer-named ones
// rot.

/** Default staleTime for every query unless overridden by the query family. */
export const DEFAULT_STALE_TIME_MS = 5 * 60_000; // 5 minutes

/** Default retry count for queries. React Query default is 3; we trim to 2
 *  because the auth-flow reauth interceptor already handles 401s and we do
 *  not want to amplify transient 5xx into a visible retry loop in the UI. */
export const DEFAULT_QUERY_RETRY = 2;

/** Mutations are writes. Retrying after a timeout can repeat a request that
 *  already reached the API, so callers must opt in explicitly for a
 *  documented idempotent operation. Do not change this default. */
export const MUTATION_RETRY = false;

/** Background refetch cadence for the job list. Picked to feel "live" without
 *  doubling backend load — at 60s the user sees new assignments within a
 *  minute while we keep requests bounded. */
export const JOB_LIST_REFETCH_INTERVAL_MS = 60_000;

/** Jobs are mutated from many places (assignment, status, completion). 30s
 *  keeps the list feeling current when the user takes a single action then
 *  navigates back without waiting for the next refetch tick. */
export const JOB_LIST_STALE_TIME_MS = 30_000;

/** 30 min is long enough that the list survives a back-to-back revisit
 *  without re-hitting the API, short enough that closed tabs do not pin
 *  data indefinitely. */
export const JOB_LIST_GC_TIME_MS = 30 * 60_000;

/** Fallback cadence for the in-app notification history. Push receipt messages
 *  normally invalidate the query immediately; polling also covers browsers
 *  where push is unsupported, denied or temporarily unavailable. */
export const NOTIFICATION_LIST_REFETCH_INTERVAL_MS = 60_000;

/** Keep notification data briefly fresh so reopening the drawer does not
 *  duplicate a request immediately after a push-triggered refresh. */
export const NOTIFICATION_LIST_STALE_TIME_MS = 15_000;

/** Notification history is user-scoped and inexpensive to reload. A shorter
 *  lifetime limits how long inactive user/session data remains in memory. */
export const NOTIFICATION_LIST_GC_TIME_MS = 15 * 60_000;
