import type { QueryClient, QueryKey } from '@tanstack/react-query';
import { JobStatus, type JobListItemViewModel } from '../../../api/generated/models';
import { getPaginatedListInitialState, buildPaginatedListQueryKey } from '../../../hooks/paginatedListState';
import { apiClient } from '../../../lib/axios';

export const JOB_LIST_PAGE_SIZE = 20;
export const JOB_LIST_STALE_TIME_MS = 30_000;
export const JOB_LIST_GC_TIME_MS = 30 * 60 * 1000;

const JOB_LIST_STORAGE_KEY = 'jobs';
const JOB_STATUS_SECTION_KEY = 'mine-jobs';
const STATUS_FILTER_LAST_ACTIVE_KEY = 'statusFilter:lastActive';

export interface JobListPage {
  items: JobListItemViewModel[];
  totalCount: number;
}

export interface JobListPageRequest {
  limit: number;
  offset: number;
  search?: string;
  sortBy?: string;
  sortDirection?: string;
}

export function getJobListBaseQueryKey(statuses: JobStatus[]): QueryKey {
  return ['/api/jobs', { status: statuses }];
}

export async function fetchJobListPage(
  statuses: JobStatus[],
  { limit, offset, search, sortBy, sortDirection }: JobListPageRequest,
): Promise<JobListPage> {
  return await apiClient.get('/api/jobs', {
    params: {
      status: statuses,
      search: search || undefined,
      sortBy: sortBy || undefined,
      sortDirection: sortDirection || undefined,
      limit,
      offset,
    },
  }) as JobListPage;
}

function getInitialJobStatuses(): JobStatus[] {
  const defaults = [JobStatus.Draft];

  if (typeof sessionStorage === 'undefined') return defaults;

  try {
    if (sessionStorage.getItem(STATUS_FILTER_LAST_ACTIVE_KEY) !== JOB_STATUS_SECTION_KEY) {
      return defaults;
    }

    const saved = sessionStorage.getItem(`statusFilter:${JOB_STATUS_SECTION_KEY}`);
    if (!saved) return defaults;

    const parsed = JSON.parse(saved);
    return Array.isArray(parsed) && parsed.length > 0 ? parsed as JobStatus[] : defaults;
  } catch {
    return defaults;
  }
}

function getNextPageParam(lastPage: JobListPage, allPages: JobListPage[]): number | undefined {
  const loadedCount = allPages.reduce((sum, page) => sum + page.items.length, 0);
  return loadedCount >= lastPage.totalCount ? undefined : loadedCount;
}

export async function prefetchInitialJobList(queryClient: QueryClient): Promise<void> {
  const statuses = getInitialJobStatuses();
  const initialState = getPaginatedListInitialState(JOB_LIST_STORAGE_KEY);
  const queryKey = buildPaginatedListQueryKey(
    getJobListBaseQueryKey(statuses),
    initialState.search,
    initialState.sort,
    JOB_LIST_PAGE_SIZE,
  );

  await queryClient.prefetchInfiniteQuery({
    queryKey,
    queryFn: ({ pageParam }) => fetchJobListPage(statuses, {
      limit: JOB_LIST_PAGE_SIZE,
      offset: pageParam,
      search: initialState.search || undefined,
      sortBy: initialState.sort.field || undefined,
      sortDirection: initialState.sort.direction,
    }),
    initialPageParam: 0,
    getNextPageParam,
    staleTime: JOB_LIST_STALE_TIME_MS,
    gcTime: JOB_LIST_GC_TIME_MS,
  });
}
