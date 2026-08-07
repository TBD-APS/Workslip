import { useCallback, useMemo, useRef } from 'react';
import {
  useInfiniteQuery,
  useQueryClient,
  type InfiniteData,
  type QueryKey,
} from '@tanstack/react-query';

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
  const queryClient = useQueryClient();
  const refreshPromiseRef = useRef<Promise<void> | null>(null);
  const query = useInfiniteQuery({
    queryKey,
    queryFn: ({ pageParam }) => fetchPage({ limit: pageSize, offset: pageParam }),
    initialPageParam: 0,
    placeholderData: (previousData) => previousData,
    getNextPageParam: (lastPage, allPages) => {
      const loadedCount = allPages.reduce((sum, page) => sum + page.items.length, 0);
      if (loadedCount >= lastPage.totalCount) {
        return undefined;
      }
      return loadedCount;
    },
    enabled,
  });
  const refetchQuery = query.refetch;

  const items = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data]);
  const totalCount = query.data?.pages[0]?.totalCount ?? 0;

  const refreshFirstPage = useCallback((): Promise<void> => {
    if (refreshPromiseRef.current) return refreshPromiseRef.current;

    queryClient.setQueryData<InfiniteData<PageData<TItem>, number>>(queryKey, (currentData) => {
      if (!currentData || currentData.pages.length <= 1) return currentData;

      return {
        pages: currentData.pages.slice(0, 1),
        pageParams: currentData.pageParams.slice(0, 1),
      };
    });

    const refreshPromise = refetchQuery({ cancelRefetch: true })
      .then(() => undefined)
      .finally(() => {
        refreshPromiseRef.current = null;
      });

    refreshPromiseRef.current = refreshPromise;
    return refreshPromise;
  }, [queryClient, queryKey, refetchQuery]);

  return {
    ...query,
    items,
    totalCount,
    refetch: refreshFirstPage,
    refreshFirstPage,
    isLoadingMore: query.isFetchingNextPage,
    isFetching: query.isFetching,
  };
}
