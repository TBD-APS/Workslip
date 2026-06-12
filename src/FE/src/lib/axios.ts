import axios from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { toast } from 'sonner';
import qs from 'qs';
import { AUTH_TOKEN_KEY, USER_EMAIL_KEY } from '../providers/authContextValue';

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
  if (token && !config.headers.Authorization) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  config.headers.Accept = 'application/json';
  return config;
});

apiClient.interceptors.response.use(
  (response) => {
    if (response.config.responseType === 'blob') {
      return response;
    }
    return response.data;
  },
  (error) => {
    const isCanceled =
      axios.isCancel(error) ||
      error?.name === 'CanceledError' ||
      error?.code === 'ERR_CANCELED' ||
      error?.message === 'canceled';

    // Silent fail for cancellations - this is normal behavior for React Query & unmounting
    if (isCanceled) {
      return Promise.reject(error);
    }

    const message = error.response?.data?.message || error.message;

    // Handle specific backend error patterns (from AGENTS.md rules)
    if (error.response?.status === 401) {
      const isAuthRoute = window.location.pathname.includes('/login') || window.location.pathname.includes('/invite');
      const returnTo = `${window.location.pathname}${window.location.search}${window.location.hash}`;
      sessionStorage.removeItem(AUTH_TOKEN_KEY);
      sessionStorage.removeItem(USER_EMAIL_KEY);

      if (!isAuthRoute) {
        toast.message('Fornyer login...');
        window.location.assign(`/login?reauth=1&returnTo=${encodeURIComponent(returnTo)}`);
      }
    } else if (error.response?.status === 403) {
      toast.error('Du har ikke adgang til denne handling');
    } else if (error.response?.status === 400 && error.response?.data?.errors) {
      // ValidationProblem from backend
      toast.error('Ugyldig indtastning. Tjek venligst felterne.');
    } else if (error.response?.status === 409) {
      // Conflict
      toast.error(`Konflikt: ${error.response.data.error || message}`);
    } else if (message) {
      toast.error(message);
    } else {
      toast.error('Der opstod en uventet fejl');
    }

    return Promise.reject(error);
  }
);
