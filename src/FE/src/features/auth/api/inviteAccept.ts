import { apiClient } from '../../../lib/axios';
import type { AuthTokenResponse } from '../../../api/generated/models';

export const acceptInvite = async (token: string, displayName: string, phone?: string): Promise<AuthTokenResponse> => {
  return apiClient.post(`/auth/verify-invite/${token}`, { displayName, phone });
};

export const verifyInviteToken = async (token: string): Promise<void> => {
  return apiClient.post(`/auth/invite/${token}/open`);
};
