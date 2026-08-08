import { apiClient } from '../../../lib/axios';
import { type AuthTokenResponse } from '../../../api/generated/models';

export const sendAuthCode = (email: string): Promise<void> => {
  return apiClient.post('/api/auth/send-code', { email });
};

export const verifyAuthCode = (email: string, code: string): Promise<AuthTokenResponse> => {
  return apiClient.post(`/api/auth/verify-code/${code}`, { email });
};
