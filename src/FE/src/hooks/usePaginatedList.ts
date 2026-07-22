import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useInfiniteList } from './useInfiniteList';
import { useInfiniteScroll } from './useInfiniteScroll';
import { useMediaQuery } from './useMediaQuery';

function getScrollContainer(): HTMLElement | null {
  return document.querySelector('.app-shell');
}

// Global scroll write tokens — one per storageKey.
// Only the component holding the current token may write to sessionStorage.
const scrollTokens: Record<string, symbol> = {};

interface UsePaginatedListOptions<TItem> {
  queryKey: unknown[];
  fetchPage: (params: {
    limit: number;
    offset: number;
    search?: string;
    sortBy?: string;
    sortDirection?: string;
  }) => Promise<{ items: TItem[]; totalCount: number }>;
  pageSize?: number;
  enabled?: boolean;
  storageKey?: string;
}

interface UsePaginatedListReturn<TItem> {
  items: TItem[];
  totalCount: number;
  isLoading: boolean;
  isFetching: boolean;
  isError: boolean;
  isFetchingNextPage: boolean;
  hasNextPage: boolean;
  refetch: () => void;
  fetchNextPage: () => void;
  search: string;
  handleSearchChange: (value: string) => void;
  sortBy: string;
  sortDirection: string;
  handleSort: (field: string) => void;
  viewPage: number;
  setViewPage: (page: number | ((prev: number) => number)) => void;
  totalPages: number;
  safeViewPage: number;
  pageStart: number;
  pageEnd: number;
  pageItems: TItem[];
  sentinelRef: (node: HTMLDivElement | null) => void;
  isDesktop: boolean;
}

function getInitialState(storageKey: string | undefined) {
  if (!storageKey) return { search: '', sort: { field: '', direction: 'asc' as const }, viewPage: 1 };
  const search = sessionStorage.getItem(`${storageKey}:search`) ?? '';
  let sort = { field: '', direction: 'asc' as const };
  try {
    const saved = sessionStorage.getItem(`${storageKey}:sort`);
    if (saved) sort = JSON.parse(saved);
  } catch { /* ignore */ }
  const viewPage = Number(sessionStorage.getItem(`${storageKey}:page`) ?? '1');
  return { search, sort, viewPage };
}

export function usePaginatedList<TItem>({
  queryKey,
  fetchPage,
  pageSize = 20,
  enabled = true,
  storageKey,
}: UsePaginatedListOptions<TItem>): UsePaginatedListReturn<TItem> {
  const [search, setSearch] = useState(() => getInitialState(storageKey).search);
  const [querySearch, setQuerySearch] = useState(() => getInitialState(storageKey).search);
  const [sort, setSort] = useState<{ field: string; direction: 'asc' | 'desc' }>(() => getInitialState(storageKey).sort);
  const [viewPage, setViewPage] = useState(() => getInitialState(storageKey).viewPage);
  const isDesktop = useMediaQuery('(min-width: 768px)');
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const fetchWrapped = useCallback(
    async ({ limit, offset }: { limit: number; offset: number }) => {
      const data = await fetchPage({
        limit,
        offset,
        search: querySearch || undefined,
        sortBy: sort.field || undefined,
        sortDirection: sort.direction || undefined,
      });
      return data;
    },
    [fetchPage, querySearch, sort],
  );

  const query = useInfiniteList<TItem>({
    queryKey: [...queryKey, { search: querySearch || undefined, sort, limit: pageSize }],
    fetchPage: fetchWrapped,
    pageSize,
    enabled,
  });

  const { sentinelRef } = useInfiniteScroll({
    onReachEnd: () => {
      if (query.hasNextPage && !query.isFetchingNextPage && !query.isLoading) {
        void query.fetchNextPage();
      }
    },
    enabled: Boolean(query.hasNextPage) && !query.isFetchingNextPage && !query.isLoading,
  });

  const items = query.items;
  const totalCount = query.totalCount;

  const totalPages = useMemo(
    () => Math.max(1, Math.ceil(totalCount / pageSize)),
    [totalCount, pageSize],
  );
  const safeViewPage = useMemo(
    () => Math.min(viewPage, totalPages),
    [viewPage, totalPages],
  );
  const pageStart = (safeViewPage - 1) * pageSize;
  const pageEnd = pageStart + pageSize;
  const pageItems = isDesktop ? items.slice(pageStart, pageEnd) : items;

  const handleSearchChange = useCallback((value: string) => {
    setSearch(value);
    setViewPage(1);

    if (debounceTimerRef.current) clearTimeout(debounceTimerRef.current);

    if (value.length === 0) {
      setQuerySearch('');
    } else if (value.length >= 3) {
      debounceTimerRef.current = setTimeout(() => {
        setQuerySearch(value);
      }, 300);
    }
  }, []);

  const handleSort = useCallback(
    (field: string) => {
      setSort((prev) => ({
        field,
        direction: prev.field === field
          ? (prev.direction === 'asc' ? 'desc' : 'asc')
          : 'asc',
      }));
      setViewPage(1);
    },
    [],
  );

  useEffect(() => {
    if (!isDesktop) return;
    if (items.length >= pageEnd || !query.hasNextPage || query.isFetchingNextPage || query.isLoading) return;
    void query.fetchNextPage();
  }, [pageEnd, items.length, query.hasNextPage, query.isFetchingNextPage, query.isLoading, isDesktop]);

  const prevSearchRef = useRef(search);
  const prevSortRef = useRef(JSON.stringify(sort));

  useEffect(() => {
    if (search !== prevSearchRef.current || JSON.stringify(sort) !== prevSortRef.current) {
      setViewPage(1);
      prevSearchRef.current = search;
      prevSortRef.current = JSON.stringify(sort);
    }
  }, [search, sort]);

  useEffect(() => {
    if (!storageKey) return;
    sessionStorage.setItem(`${storageKey}:search`, search);
    sessionStorage.setItem(`${storageKey}:sort`, JSON.stringify(sort));
    sessionStorage.setItem(`${storageKey}:page`, String(viewPage));
  }, [storageKey, search, sort, viewPage]);

  // Scroll position restore on mount
  const isLoading = query.isLoading;
  useEffect(() => {
    if (!storageKey || isLoading) return;
    const saved = sessionStorage.getItem(`${storageKey}:scroll`);
    if (saved) {
      requestAnimationFrame(() => getScrollContainer()?.scrollTo({ top: Number(saved) }));
    }
  }, [storageKey, isLoading]);

  // Scroll position save — debounced to sessionStorage.
  // A global token ensures only the LATEST component instance per storageKey
  // can write. When a new instance mounts it claims the token, so the old
  // instance's listener (still briefly attached during React's transition)
  // cannot overwrite the saved position with the new page's scroll value.
  useEffect(() => {
    if (!storageKey) return;
    const container = getScrollContainer();
    if (!container) return;

    const myToken = Symbol();
    scrollTokens[storageKey] = myToken;

    let timer: ReturnType<typeof setTimeout> | undefined;
    const onScroll = () => {
      if (scrollTokens[storageKey] !== myToken) return;
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => {
        if (scrollTokens[storageKey] !== myToken) return;
        sessionStorage.setItem(`${storageKey}:scroll`, String(container.scrollTop));
      }, 200);
    };
    container.addEventListener('scroll', onScroll, { passive: true });
    return () => {
      if (timer) clearTimeout(timer);
      container.removeEventListener('scroll', onScroll);
      if (scrollTokens[storageKey] === myToken) {
        delete scrollTokens[storageKey];
      }
    };
  }, [storageKey]);

  return {
    items,
    totalCount,
    isLoading: query.isLoading,
    isFetching: query.isFetching,
    isError: query.isError,
    isFetchingNextPage: query.isFetchingNextPage,
    hasNextPage: query.hasNextPage,
    refetch: () => void query.refetch(),
    fetchNextPage: () => void query.fetchNextPage(),
    search,
    handleSearchChange,
    sortBy: sort.field,
    sortDirection: sort.direction,
    handleSort,
    viewPage,
    setViewPage,
    totalPages,
    safeViewPage,
    pageStart,
    pageEnd,
    pageItems,
    sentinelRef,
    isDesktop,
  };
}
