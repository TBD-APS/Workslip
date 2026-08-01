import type { QueryClient } from '@tanstack/react-query';
import { apiClient } from '../../../lib/axios';
import { getGetApiJobsQueryKey } from '../../../api/generated/jobs/jobs';

export const COMPLETED_JOB_VIEW_TYPE = 'Completed';

export function markJobAsSeen(id: string, queryClient?: QueryClient, viewType?: string): void {
  const params = viewType ? { viewType } : undefined;
  apiClient
    .post(`/api/jobs/${id}/seen`, undefined, { params })
    .then(() => {
      queryClient?.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
    })
    .catch(() => {});
}
