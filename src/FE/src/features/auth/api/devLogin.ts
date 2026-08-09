import { apiClient } from '../../../lib/axios';
import type { AuthTokenResponse } from '../../../api/generated/models';

export const getDevToken = (email: string): Promise<AuthTokenResponse> => {
  return apiClient.post('/api/dev/token', { email });
};
