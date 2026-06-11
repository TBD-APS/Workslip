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

export const getDevToken = (email: string): Promise<AuthTokenResponse> => {
  return apiClient.post('/api/dev/token', { email });
};

export const sendAuthCode = (email: string): Promise<void> => {
  return apiClient.post('/api/auth/send-code', { email });
};

export const verifyAuthCode = (email: string, code: string): Promise<AuthTokenResponse> => {
  return apiClient.post(`/api/auth/verify-code/${code}`, { email });
};
