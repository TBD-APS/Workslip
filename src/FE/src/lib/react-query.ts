import { QueryClient } from '@tanstack/react-query';
import { JOB_LIST_GC_TIME_MS, JOB_LIST_STALE_TIME_MS } from '../features/jobs/queries/jobListQuery';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 2,
      staleTime: 1000 * 60 * 5, // 5 minutes
      refetchOnWindowFocus: false, // Don't annoy the user if they tab away and back
    },
    mutations: {
      // Mutations are writes. Retrying after a timeout can repeat a request
      // that already reached the API, so callers must opt in explicitly for
      // a documented idempotent operation.
      retry: false,
    },
  },
});

// The jobs home screen should render cached data immediately when revisited,
// then refresh in the background once the short freshness window has elapsed.
queryClient.setQueryDefaults(['/api/jobs'], {
  staleTime: JOB_LIST_STALE_TIME_MS,
  gcTime: JOB_LIST_GC_TIME_MS,
});
