import { QueryClient } from '@tanstack/react-query';

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
