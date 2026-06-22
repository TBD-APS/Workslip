import { apiClient } from '../../../lib/axios';

export interface InviteOpenResponse {
  email: string;
  userExists: boolean;
  consumed: boolean;
}

export const verifyInviteToken = (token: string): Promise<InviteOpenResponse> => {
  return apiClient.post(`/api/auth/invite/${token}/open`);
};
