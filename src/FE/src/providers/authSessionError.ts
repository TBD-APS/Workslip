import axios from 'axios';

export function isRejectedAuthSession(error: unknown): boolean {
  return axios.isAxiosError(error) && error.response?.status === 401;
}

export function shouldRetryAuthSession(failureCount: number, error: unknown): boolean {
  if (isRejectedAuthSession(error)) return false;
  return failureCount < 1;
}
