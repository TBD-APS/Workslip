import { apiClient } from '../../../lib/axios';
import type { AuthTokenResponse } from '../../../api/generated/models';

export const getDemoToken = (): Promise<AuthTokenResponse> => {
  return apiClient.post('/api/demo/token');
};
