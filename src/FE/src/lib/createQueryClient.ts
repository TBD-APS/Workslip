import { QueryClient } from '@tanstack/react-query';
import { NOTIFICATION_QUERY_PREFIX } from './notificationQueryKeys';
import {
  DEFAULT_QUERY_RETRY,
  DEFAULT_STALE_TIME_MS,
  JOB_LIST_GC_TIME_MS,
  JOB_LIST_REFETCH_INTERVAL_MS,
  JOB_LIST_STALE_TIME_MS,
  MUTATION_RETRY,
  NOTIFICATION_LIST_GC_TIME_MS,
  NOTIFICATION_LIST_REFETCH_INTERVAL_MS,
  NOTIFICATION_LIST_STALE_TIME_MS,
} from './queryTimings';

// The factory and the default instance are split so tests can build a fresh
// client per case (one with frozen time, one with no retries, etc.) without
// the module-level singleton getting in the way. Production code should
// import the default `queryClient` from `./react-query`, not call this
// factory directly.
//
// Query-family defaults belong here instead of in feature components. This
// keeps polling, focus refresh and cache lifetime decisions visible in one
// place and prevents multiple observers from accidentally using conflicting
// freshness behavior.
//
// Decisions documented inline:
// - `refetchOnReconnect: true` is the React Query default but is set
//   explicitly because a future global override (e.g. offline-mode work)
//   would otherwise silently change behaviour for every query.
// - `networkMode` is left at the React Query default `'online'`. PWA offline
//   support is a separate piece of work; if you flip this, audit the
//   service-worker and the API cache layer together.
// - `throwOnError: false` is the React Query default. Workslip uses
//   per-mutation `onError` + a top-level ErrorBoundary for failures;
//   flipping this globally would crash the app on any unhandled query
//   rejection, so it is documented as a deliberate choice.
export function createQueryClient(): QueryClient {
  const client = new QueryClient({
    defaultOptions: {
      queries: {
        retry: DEFAULT_QUERY_RETRY,
        staleTime: DEFAULT_STALE_TIME_MS,
        refetchOnWindowFocus: false,
        refetchOnReconnect: true,
      },
      mutations: {
        retry: MUTATION_RETRY,
      },
    },
  });

  // The job list must silently pick up new assignments and status changes
  // while the app is open. `refetchInterval` handles the steady-state case;
  // `refetchIntervalInBackground` keeps the poll running while the tab is
  // not focused (React Query's default is to skip the interval tick there, so
  // a backgrounded open app would otherwise show stale statuses); and
  // `refetchOnWindowFocus` catches the user who switched tabs for several
  // minutes and would otherwise be looking at stale data on return.
  client.setQueryDefaults(['/api/jobs'], {
    staleTime: JOB_LIST_STALE_TIME_MS,
    gcTime: JOB_LIST_GC_TIME_MS,
    refetchInterval: JOB_LIST_REFETCH_INTERVAL_MS,
    refetchIntervalInBackground: true,
    refetchOnWindowFocus: true,
  });

  // The bell is visible while the authenticated layout is mounted, even when
  // the drawer is closed. Push receipt invalidation gives the normal immediate
  // path; background polling and focus refresh cover denied/unsupported push,
  // delivery gaps and a device returning online after a notification was
  // queued. Each concrete query key includes both user and organization scope.
  client.setQueryDefaults(NOTIFICATION_QUERY_PREFIX, {
    staleTime: NOTIFICATION_LIST_STALE_TIME_MS,
    gcTime: NOTIFICATION_LIST_GC_TIME_MS,
    refetchInterval: NOTIFICATION_LIST_REFETCH_INTERVAL_MS,
    refetchIntervalInBackground: true,
    refetchOnWindowFocus: true,
  });

  return client;
}
