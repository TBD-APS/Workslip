import axios from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { notify } from './toast';
import {
  consumePendingInteraction,
  createCorrelationId,
  trackApiDependency,
  trackUserInteraction,
} from '../applicationInsights';
import {
  isDelegatedOrganizationSessionToken,
  restoreHomeOrganizationSession,
} from '../features/superadmin/organizationSession';

declare module 'axios' {
  export interface AxiosRequestConfig {
    skipGlobalErrorToast?: boolean;
    correlationId?: string;
    telemetryAction?: string;
    telemetryStartedAt?: number;
    idempotencyKey?: string;
    skipAuthRefresh?: boolean;
    _authRetry?: boolean;
  }
}
import qs from 'qs';
import {
  AUTH_TOKEN_KEY,
  AuthStorage,
  clearReauthInFlight,
  isReauthInFlight,
  setReauthInFlight,
} from '../providers/authContextValue';

const configuredApiUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? '';
const hostname = typeof window === 'undefined' ? '' : window.location.hostname;
const isVercelHosted = hostname === 'app.mrsoftware.dk' || hostname.endsWith('.vercel.app');
const apiUrl = isVercelHosted ? '' : configuredApiUrl;
const AUTH_ME_TIMEOUT_MS = 6_000;

const isAuthMeRequest = (url: string | undefined): boolean => {
  const normalizedUrl = (url ?? '')
    .split('?')[0]
    .toLowerCase()
    .replace(/\/+$/, '');

  return normalizedUrl.endsWith('/api/auth/me');
};

const getRequestBearerToken = (config: InternalAxiosRequestConfig | undefined): string | null => {
  const authorization = config?.headers?.get?.('Authorization') ?? config?.headers?.Authorization;
  if (typeof authorization !== 'string') return null;

  const match = /^Bearer\s+(.+)$/i.exec(authorization.trim());
  return match?.[1] ?? null;
};

export const apiClient = axios.create({
  baseURL: apiUrl,
  withCredentials: true,
  paramsSerializer: {
    serialize: (params) =>
      qs.stringify(params, {
        arrayFormat: 'repeat',
      }),
  },
});

interface RefreshedSession {
  token: string;
  user: { email: string };
}

let refreshPromise: Promise<string> | null = null;

const waitForConcurrentRotation = () => new Promise((resolve) => window.setTimeout(resolve, 250));

async function refreshAccessToken(): Promise<string> {
  if (refreshPromise) return refreshPromise;

  refreshPromise = (async () => {
    for (let attempt = 0; attempt < 2; attempt += 1) {
      try {
        const session = await apiClient.post<unknown, RefreshedSession>('/api/auth/refresh', undefined, {
          skipAuthRefresh: true,
          skipGlobalErrorToast: true,
        });
        AuthStorage.setItem(AUTH_TOKEN_KEY, session.token);
        clearReauthInFlight();
        return session.token;
      } catch (error) {
        if (axios.isAxiosError(error) && error.response?.status === 409 && attempt === 0) {
          await waitForConcurrentRotation();
          continue;
        }
        throw error;
      }
    }
    throw new Error('Session refresh did not complete');
  })().finally(() => {
    refreshPromise = null;
  });

  return refreshPromise;
}

export async function revokeServerSession(): Promise<void> {
  await apiClient.post('/api/auth/logout', undefined, {
    skipAuthRefresh: true,
    skipGlobalErrorToast: true,
  });
}

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
  // Bound only the startup identity request. Other reports and mutations retain
  // their existing timeout behaviour.
  if (isAuthMeRequest(config.url) && (!config.timeout || config.timeout <= 0)) {
    config.timeout = AUTH_ME_TIMEOUT_MS;
    config.skipGlobalErrorToast = true;
  }

  const token = AuthStorage.getItem(AUTH_TOKEN_KEY);
  if (token && !config.headers.Authorization) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  config.headers.Accept = 'application/json';

  const interaction = consumePendingInteraction();
  const method = (config.method ?? 'get').toLowerCase();
  config.correlationId = config.correlationId ?? interaction?.correlationId ?? createCorrelationId();
  config.telemetryAction = config.telemetryAction ?? interaction?.action;
  config.telemetryStartedAt = Date.now();
  config.headers['X-Correlation-ID'] = config.correlationId;
  if (mutatingMethods.has(method)) {
    config.idempotencyKey = config.idempotencyKey ?? config.correlationId;
    config.headers['Idempotency-Key'] = config.idempotencyKey;
  }

  if (interaction && mutatingMethods.has(method)) {
    trackUserInteraction(interaction.action, interaction.correlationId);
  }

  const requestMethod = (config.method ?? 'get').toLowerCase();
  if (mutatingMethods.has(requestMethod)) {
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
    if (mutatingMethods.has(response.config.method ?? 'get')) {
      trackApiDependency({
        correlationId: response.config.correlationId!,
        action: response.config.telemetryAction,
        method: response.config.method ?? 'get',
        url: response.config.url ?? '',
        durationMs: Date.now() - (response.config.telemetryStartedAt ?? Date.now()),
        responseCode: response.status,
        success: response.status >= 200 && response.status < 400,
      });
    }
    if (response.config.responseType === 'blob') {
      return response;
    }
    return response.data;
  },
  async (error) => {
    if (error.config) releaseKey(error.config);
    if (error.config && mutatingMethods.has(error.config.method ?? 'get')) {
      trackApiDependency({
        correlationId: error.config.correlationId!,
        action: error.config.telemetryAction,
        method: error.config.method ?? 'get',
        url: error.config.url ?? '',
        durationMs: Date.now() - (error.config.telemetryStartedAt ?? Date.now()),
        responseCode: error.response?.status,
        success: false,
      });
    }

    const isCanceled =
      axios.isCancel(error) ||
      error?.name === 'CanceledError' ||
      error?.code === 'ERR_CANCELED' ||
      error?.message === 'canceled';

    if (isCanceled) {
      return Promise.reject(error);
    }

    if (error?.message === DUPLICATE_REQUEST_ERROR) {
      return Promise.reject(error);
    }

    const message = error.response?.data?.message || error.message;
    const requestUrl = (error.config?.url ?? '').toLowerCase();
    const isAuthApi = requestUrl.includes('/api/auth/');
    const isAuthRoute = window.location.pathname.includes('/login') || window.location.pathname.includes('/invite');
    const isMeEndpoint = isAuthMeRequest(error.config?.url);

    if (error.response?.status === 401) {
      const requestToken = getRequestBearerToken(error.config);
      const activeToken = AuthStorage.getItem(AUTH_TOKEN_KEY);
      const isStaleUnauthorizedResponse = Boolean(
        requestToken && activeToken && requestToken !== activeToken,
      );
      const shouldHandleSessionExpiry = isMeEndpoint || (!isAuthApi && !isAuthRoute);

      if (isStaleUnauthorizedResponse) {
        error.config._authRetry = true;
        error.config.headers.Authorization = `Bearer ${activeToken}`;
        return apiClient.request(error.config);
      }

      if (
        shouldHandleSessionExpiry
        && isDelegatedOrganizationSessionToken(requestToken)
        && restoreHomeOrganizationSession()
      ) {
        clearReauthInFlight();
        notify.dismiss();
        notify.info('Organisationssessionen er udløbet. Du er tilbage i Superadmin.');
        window.location.replace('/superadmin');
        return Promise.reject(error);
      }

      // A 401 from /api/auth/me definitively rejects the stored JWT. Treat it
      // like any other expired authenticated request instead of leaving the user
      // on the startup recovery screen. Preserve the last verified user email as
      // a reauth login hint; explicit logout/local-session cleanup still removes it.
      // Timeouts and 5xx responses remain recoverable without deleting a potentially valid session.
      if (shouldHandleSessionExpiry && !error.config?.skipAuthRefresh && !error.config?._authRetry) {
        try {
          const refreshedToken = await refreshAccessToken();
          error.config._authRetry = true;
          error.config.headers.Authorization = `Bearer ${refreshedToken}`;
          return apiClient.request(error.config);
        } catch {
          // The cookie is absent, expired, revoked, or unsafe to use. Continue
          // into the established interactive reauthentication flow below.
        }
      }

      if (shouldHandleSessionExpiry) {
        AuthStorage.removeItem(AUTH_TOKEN_KEY);

        const isReauthRoute = window.location.pathname.includes('/login')
          && new URLSearchParams(window.location.search).get('reauth') === '1';
        if (!isReauthRoute && !isReauthInFlight()) {
          setReauthInFlight();
          const returnTo = `${window.location.pathname}${window.location.search}${window.location.hash}`;
          notify.dismiss();
          window.location.replace(`/login?reauth=1&returnTo=${encodeURIComponent(returnTo)}`);
        }
      }
    } else if (isMeEndpoint || error.config?.skipGlobalErrorToast) {
      // ProtectedRoute renders recovery only for timeouts, network failures and
      // temporary server errors where the stored token may still be valid.
    } else if (error.response?.status === 403) {
      notify.error('Du har ikke adgang til denne handling');
    } else if (error.response?.status === 400 && error.response?.data?.errors) {
      notify.error('Ugyldig indtastning. Tjek venligst felterne.');
    } else if (error.response?.status === 409) {
      notify.error(`Konflikt: ${error.response.data.error || message}`);
    } else if (message) {
      notify.error(message);
    } else {
      notify.error('Der opstod en uventet fejl');
    }

    return Promise.reject(error);
  }
);
