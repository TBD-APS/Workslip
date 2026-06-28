import { useMemo } from 'react';
import { useInfiniteQuery, type QueryKey } from '@tanstack/react-query';

interface PageData<TItem> {
  items: TItem[];
  totalCount: number;
}

interface UseInfiniteListOptions<TItem> {
  queryKey: QueryKey;
  fetchPage: (params: { limit: number; offset: number }) => Promise<PageData<TItem>>;
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
      const loadedCount = allPages.reduce((sum, page) => sum + page.items.length, 0);
      if (loadedCount >= lastPage.totalCount) {
        return undefined;
      }
      return loadedCount;
    },
    enabled,
  });

  const items = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data]);
  const totalCount = query.data?.pages[0]?.totalCount ?? 0;

  return {
    ...query,
    items,
    totalCount,
    isLoadingMore: query.isFetchingNextPage,
  };
}
