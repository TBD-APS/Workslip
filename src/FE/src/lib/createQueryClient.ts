import { QueryClient } from '@tanstack/react-query';
import {
  DEFAULT_QUERY_RETRY,
  DEFAULT_STALE_TIME_MS,
  JOB_LIST_GC_TIME_MS,
  JOB_LIST_REFETCH_INTERVAL_MS,
  JOB_LIST_STALE_TIME_MS,
  MUTATION_RETRY,
} from './queryTimings';

// The factory and the default instance are split so tests can build a fresh
// client per case (one with frozen time, one with no retries, etc.) without
// the module-level singleton getting in the way. Production code should
// import the default `queryClient` from `./react-query`, not call this
// factory directly.
//
// The query-family key `['/api/jobs']` is the React Query convention for
// matching every query whose key starts with that path. It is the ONLY
// place in the app that should grow silent polling today; if another family
// needs the same treatment, add a `setQueryDefaults` call here rather than
// re-implementing polling inside the feature's hook — keeping all
// background-fetch decisions in one file makes the next audit cheap.
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
  // `refetchOnWindowFocus` catches the user who switched tabs for several
  // minutes and would otherwise be looking at stale data on return.
  client.setQueryDefaults(['/api/jobs'], {
    staleTime: JOB_LIST_STALE_TIME_MS,
    gcTime: JOB_LIST_GC_TIME_MS,
    refetchInterval: JOB_LIST_REFETCH_INTERVAL_MS,
    refetchOnWindowFocus: true,
  });

  return client;
}
