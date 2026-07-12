import axios from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { notify } from './toast';

declare module 'axios' {
  export interface AxiosRequestConfig {
    // When true, the global response interceptor will not emit an error toast.
    // Set this on requests whose callers handle (and translate) errors locally,
    // to avoid stacking the raw backend message on top of the friendly message.
    skipGlobalErrorToast?: boolean;
  }
}
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

const mutatingMethods = new Set(['post', 'put', 'patch', 'delete']);
const inFlightKeys = new Set<string>();

function requestKey(config: InternalAxiosRequestConfig): string {
  return `${(config.method ?? 'get').toUpperCase()} ${config.url}`;
}

function releaseKey(config: InternalAxiosRequestConfig): void {
  const method = config.method ?? 'get';
  if (mutatingMethods.has(method)) {
    inFlightKeys.delete(requestKey(config));
  }
}

const DUPLICATE_REQUEST_ERROR = '__DUPLICATE_REQUEST__';

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  // Attach auth token from AuthStorage (localStorage; survives PWA eviction).
  const token = AuthStorage.getItem(AUTH_TOKEN_KEY);
  if (token && !config.headers.Authorization) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  config.headers.Accept = 'application/json';

  // Deduplicate in-flight mutating requests (POST, PUT, PATCH, DELETE) by
  // method + URL. If the same request is already in-flight, reject the
  // duplicate silently. This prevents 404s from rapid double-clicks on
  // delete/update buttons before React can re-render with disabled state.
  const method = config.method ?? 'get';
  if (mutatingMethods.has(method)) {
    const key = requestKey(config);
    if (inFlightKeys.has(key)) {
      return Promise.reject(new Error(DUPLICATE_REQUEST_ERROR));
    }
    inFlightKeys.add(key);
  }

  return config;
});

apiClient.interceptors.response.use(
  (response) => {
    releaseKey(response.config);
    if (response.config.responseType === 'blob') {
      return response;
    }
    return response.data;
  },
  (error) => {
    if (error.config) releaseKey(error.config);

    const isCanceled =
      axios.isCancel(error) ||
      error?.name === 'CanceledError' ||
      error?.code === 'ERR_CANCELED' ||
      error?.message === 'canceled';

    // Silent fail for cancellations - this is normal behavior for React Query & unmounting
    if (isCanceled) {
      return Promise.reject(error);
    }

    // Silent fail for deduplicated mutating requests (rapid double-clicks)
    if (error?.message === DUPLICATE_REQUEST_ERROR) {
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
          notify.dismiss();
          // No toast: the Login page already shows its own "Genindlæser login..."
          // spinner, and adding "Fornyer login..." here would leak into the
          // user's view after they return from Microsoft.
          window.location.assign(`/login?reauth=1&returnTo=${encodeURIComponent(returnTo)}`);
        }
      }

      // Always purge stale token + email so the next render knows we are unauthenticated.
      // However, skip purging for /api/auth/me – a transient 401 here (e.g. clock
      // skew, delayed JWT propagation) should not destroy a valid token. The meQuery
      // will retry automatically, and if it still fails the user will be redirected to
      // login by ProtectedRoute.
      const isMeEndpoint = requestUrl.endsWith('/api/auth/me') || requestUrl.endsWith('/api/auth/me/');
      if (!isMeEndpoint) {
        AuthStorage.removeItem(AUTH_TOKEN_KEY);
        AuthStorage.removeItem(USER_EMAIL_KEY);
      }
    } else if (error.config?.skipGlobalErrorToast) {
      // Caller handles (and translates) this error locally. Suppress the global
      // toast so the raw backend message is not shown alongside the friendly one.
    } else if (error.response?.status === 403) {
      notify.error('Du har ikke adgang til denne handling');
    } else if (error.response?.status === 400 && error.response?.data?.errors) {
      // ValidationProblem from backend
      notify.error('Ugyldig indtastning. Tjek venligst felterne.');
    } else if (error.response?.status === 409) {
      // Conflict
      notify.error(`Konflikt: ${error.response.data.error || message}`);
    } else if (message) {
      notify.error(message);
    } else {
      notify.error('Der opstod en uventet fejl');
    }

    return Promise.reject(error);
  }
);
