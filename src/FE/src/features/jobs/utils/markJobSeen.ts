import type { QueryClient } from '@tanstack/react-query';
import { apiClient } from '../../../lib/axios';
import { getGetApiJobsQueryKey } from '../../../api/generated/jobs/jobs';

export function markJobAsSeen(id: string, queryClient?: QueryClient): void {
  apiClient
    .post(`/api/jobs/${id}/seen`)
    .then(() => {
      queryClient?.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
    })
    .catch(() => {});
}
