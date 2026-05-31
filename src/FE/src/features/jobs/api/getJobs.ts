import { apiClient } from '../../../lib/axios';
import type { JobListItemViewModel } from '../types';

export const getJobs = async (): Promise<JobListItemViewModel[]> => {
  return apiClient.get('/jobs');
};
