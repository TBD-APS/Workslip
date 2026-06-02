import { apiClient } from '../../../lib/axios';
import type { JobListItemViewModel } from '../hooks/responses';

export const getJobs = async (): Promise<JobListItemViewModel[]> => {
  return apiClient.get('/jobs');
};
