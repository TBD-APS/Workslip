import axios from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { toast } from 'sonner';
import qs from 'qs';
import {
  AUTH_TOKEN_KEY,
  USER_EMAIL_KEY,
  AuthStorage,
  isReauthInFlight,
  setReauthInFlight,
} from '../providers/authContextValue';

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
  // Attach auth token from AuthStorage (localStorage; survives PWA eviction).
  const token = AuthStorage.getItem(AUTH_TOKEN_KEY);
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
      const requestUrl = (error.config?.url ?? '').toLowerCase();
      const isAuthApi = requestUrl.includes('/api/auth/');
      const isAuthRoute = window.location.pathname.includes('/login') || window.location.pathname.includes('/invite');

      // /api/auth/* failures (e.g. /api/auth/entra-login with a bad Microsoft token)
      // must NOT trigger a reauth redirect — that would loop forever.
      if (!isAuthApi && !isAuthRoute) {
        // Gate concurrent 401s so we redirect exactly once per expiry.
        // Without this, every in-flight React Query mutation that fires
        // a 401 within milliseconds would race to window.location.assign(...),
        // producing multiple toasts and reloads. The flag is TTL-bounded
        // (see setReauthInFlight) so a stuck redirect self-heals.
        if (!isReauthInFlight()) {
          setReauthInFlight();
          const returnTo = `${window.location.pathname}${window.location.search}${window.location.hash}`;
          // Dismiss any leftover toasts before the page navigation — Sonner
          // toasts otherwise persist across navigations and can show stale
          // errors from the previous page once the user returns from Microsoft.
          toast.dismiss();
          // No toast: the Login page already shows its own "Genindlæser login..."
          // spinner, and adding "Fornyer login..." here would leak into the
          // user's view after they return from Microsoft.
          window.location.assign(`/login?reauth=1&returnTo=${encodeURIComponent(returnTo)}`);
        }
      }

      // Always purge stale token + email so the next render knows we are unauthenticated.
      AuthStorage.removeItem(AUTH_TOKEN_KEY);
      AuthStorage.removeItem(USER_EMAIL_KEY);
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
