import type { QueryKey } from '@tanstack/react-query';

export interface PaginatedListSort {
  field: string;
  direction: 'asc' | 'desc';
}

export interface PaginatedListInitialState {
  search: string;
  sort: PaginatedListSort;
  viewPage: number;
}

const DEFAULT_SORT: PaginatedListSort = { field: '', direction: 'asc' };

export function getPaginatedListInitialState(storageKey?: string): PaginatedListInitialState {
  if (!storageKey || typeof sessionStorage === 'undefined') {
    return { search: '', sort: DEFAULT_SORT, viewPage: 1 };
  }

  const search = sessionStorage.getItem(`${storageKey}:search`) ?? '';
  let sort = DEFAULT_SORT;

  try {
    const saved = sessionStorage.getItem(`${storageKey}:sort`);
    if (saved) {
      const parsed = JSON.parse(saved) as Partial<PaginatedListSort>;
      if (
        typeof parsed.field === 'string'
        && (parsed.direction === 'asc' || parsed.direction === 'desc')
      ) {
        sort = { field: parsed.field, direction: parsed.direction };
      }
    }
  } catch {
    // Ignore invalid browser state and use the default sort.
  }

  const storedPage = Number(sessionStorage.getItem(`${storageKey}:page`) ?? '1');
  const viewPage = Number.isFinite(storedPage) && storedPage > 0 ? Math.floor(storedPage) : 1;

  return { search, sort, viewPage };
}

export function buildPaginatedListQueryKey(
  baseQueryKey: QueryKey,
  querySearch: string,
  sort: PaginatedListSort,
  pageSize: number,
): QueryKey {
  return [
    ...baseQueryKey,
    {
      search: querySearch || undefined,
      sort,
      limit: pageSize,
    },
  ];
}
