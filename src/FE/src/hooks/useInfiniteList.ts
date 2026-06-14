import { useMemo } from 'react';
import { useInfiniteQuery, type QueryKey } from '@tanstack/react-query';

interface UseInfiniteListOptions<TItem> {
  queryKey: QueryKey;
  fetchPage: (params: { limit: number; offset: number }) => Promise<TItem[]>;
  pageSize?: number;
  enabled?: boolean;
}

export function useInfiniteList<TItem>({
  queryKey,
  fetchPage,
  pageSize = 50,
  enabled = true,
}: UseInfiniteListOptions<TItem>) {
  const query = useInfiniteQuery({
    queryKey,
    queryFn: ({ pageParam }) => fetchPage({ limit: pageSize, offset: pageParam }),
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) => {
      if (lastPage.length < pageSize) {
        return undefined;
      }

      return allPages.reduce((offset, page) => offset + page.length, 0);
    },
    enabled,
  });

  const items = useMemo(() => query.data?.pages.flatMap((page) => page) ?? [], [query.data]);

  return {
    ...query,
    items,
    isLoadingMore: query.isFetchingNextPage,
  };
}
