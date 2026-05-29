import { apiClient } from '../../../lib/axios';

export interface AuthUserResponse {
  userId: string;
  organizationId: string;
  email: string;
  displayName: string;
  role: string;
}

export interface DevTokenResponse {
  token: string;
  tokenType: string;
  expiresIn: number;
  user: AuthUserResponse;
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

export const getDevToken = async (email: string): Promise<DevTokenResponse> => {
  return apiClient.post('/dev/token', { email });
};

export const sendAuthCode = async (email: string): Promise<void> => {
  return apiClient.post('/auth/send-code', { email });
};

export const verifyAuthCode = async (email: string, code: string): Promise<AuthCodeResponse> => {
  return apiClient.post('/auth/verify-code', { email, code });
};
