// Cache-timing constants for the React Query layer.
//
// Two layers of freshness live here:
//
// 1. `DEFAULT_*` — applied to every query in the app unless a query family
//    overrides them via `setQueryDefaults` or a hook passes its own options.
//    Keep these conservative; per-feature overrides are cheaper than raising
//    the global cost.
//
// 2. `JOB_LIST_*` — applied to the `['/api/jobs']` query family. The job list
//    is the only screen in the current MVP that needs near-real-time
//    updates (assignments, status changes) so it lives in its own bucket.
//    Other families that later need the same treatment should follow the
//    `setQueryDefaults` pattern in `createQueryClient.ts` rather than
//    growing the constants here.
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
