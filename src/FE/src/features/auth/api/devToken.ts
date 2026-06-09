import { apiClient } from '../../../lib/axios';
import { type AuthTokenResponse } from '../../../api/generated/models';

export interface AuthUserResponse {
  userId: string;
  organizationId: string;
  email: string;
  displayName: string;
  role: string;
}

export interface AuthCodeResponse {
  token: string;
  user: {
    email: string;
  };
}

export interface DevTokenRequest {
  email: string;
}

export const getDevToken = async (email: string): Promise<AuthTokenResponse> => {
  return apiClient.post('/dev/token', { email });
};

export const sendAuthCode = async (email: string): Promise<void> => {
  return apiClient.post('/auth/send-code', { email });
};

export const verifyAuthCode = async (email: string, code: string): Promise<AuthCodeResponse> => {
  return apiClient.post('/auth/verify-code', { email, code });
};
