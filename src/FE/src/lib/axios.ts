import axios from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { toast } from 'sonner';
import qs from 'qs';
import { AUTH_TOKEN_KEY } from '../providers/authContextValue';

const apiUrl = import.meta.env.VITE_API_BASE_URL ?? '';

export const apiClient = axios.create({
  baseURL: apiUrl,
  paramsSerializer: {
    serialize: (params) =>
      qs.stringify(params, {
        arrayFormat: 'repeat',
      }),
  },
});

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  // Attach auth token from sessionStorage
  const token = sessionStorage.getItem(AUTH_TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  config.headers.Accept = 'application/json';
  return config;
});

apiClient.interceptors.response.use(
  (response) => {
    return response.data;
  },
  (error) => {
    if (
      axios.isCancel(error) ||
      error?.name === 'CanceledError' ||
      error?.code === 'ERR_CANCELED' ||
      error?.message?.toLowerCase().includes('cancel') ||
      error?.message?.toLowerCase().includes('abort')
    ) {
      return Promise.reject(error);
    }

    const message = error.response?.data?.message || error.message;
    
    // Handle specific backend error patterns (from AGENTS.md rules)
    if (error.response?.status === 401) {
      toast.error('Log ind for at fortsætte');
      sessionStorage.removeItem(AUTH_TOKEN_KEY);
      window.location.assign('/login');
    } else if (error.response?.status === 403) {
      toast.error('Du har ikke adgang til denne handling');
    } else if (error.response?.status === 400 && error.response?.data?.errors) {
      // ValidationProblem from backend
      toast.error('Ugyldig indtastning. Tjek venligst felterne.');
    } else if (error.response?.status === 409) {
      // Conflict
      toast.error(`Konflikt: ${error.response.data.error || message}`);
    } else {
      toast.error(message || 'Der opstod en uventet fejl');
    }
    
    return Promise.reject(error);
  }
);
