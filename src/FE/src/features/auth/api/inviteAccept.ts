import { apiClient } from '../../../lib/axios';
import type { AuthTokenResponse } from '../../../api/generated/models';

export const acceptInvite = (token: string, displayName: string, phone?: string): Promise<AuthTokenResponse> => {
  return apiClient.post(`/api/auth/verify-invite/${token}`, { displayName, phone });
};

export const verifyInviteToken = (token: string): Promise<void> => {
  return apiClient.post(`/api/auth/invite/${token}/open`);
};
