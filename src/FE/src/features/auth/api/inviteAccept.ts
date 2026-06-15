import { apiClient } from '../../../lib/axios';

export const verifyInviteToken = (token: string): Promise<void> => {
  return apiClient.post(`/api/auth/invite/${token}/open`);
};
