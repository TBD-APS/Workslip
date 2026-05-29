import { QueryClient } from '@tanstack/react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 2,
      staleTime: 1000 * 60 * 5, // 5 minutes
      refetchOnWindowFocus: false, // Don't annoy the user if they tab away and back
    },
    mutations: {
      retry: 1,
    }
  },
});
