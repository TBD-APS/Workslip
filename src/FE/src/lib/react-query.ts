// Thin re-export module for the React Query layer.
//
// New code should prefer importing directly from `createQueryClient` and
// `queryTimings` so the dependency on the singleton is explicit. This file
// is kept as a stable surface for the existing two import sites
// (`features/jobs/queries/jobListQuery` and
// `providers/AuthenticatedAppProvider`) — when those are migrated, this
// file can be removed.

export { createQueryClient } from './createQueryClient';
export {
  DEFAULT_QUERY_RETRY,
  DEFAULT_STALE_TIME_MS,
  JOB_LIST_GC_TIME_MS,
  JOB_LIST_REFETCH_INTERVAL_MS,
  JOB_LIST_STALE_TIME_MS,
  MUTATION_RETRY,
} from './queryTimings';

import { createQueryClient } from './createQueryClient';

export const queryClient = createQueryClient();
