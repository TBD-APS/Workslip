import { AxiosError, AxiosHeaders, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./toast', () => ({
  notify: { error: vi.fn(), info: vi.fn(), dismiss: vi.fn() },
}));

vi.mock('../applicationInsights', () => ({
  consumePendingInteraction: () => undefined,
  createCorrelationId: () => 'test-correlation-id',
  trackApiDependency: vi.fn(),
  trackUserInteraction: vi.fn(),
}));

import { apiClient, REFRESH_UNAVAILABLE_MESSAGE } from './axios';
import { notify } from './toast';
import { AUTH_TOKEN_KEY, AuthStorage } from '../providers/authContextValue';

type RefreshOutcome = 'ok' | 'network' | 'server-error' | 'rejected';

const respond = (config: InternalAxiosRequestConfig, status: number, data: unknown) => {
  const response = { data, status, statusText: String(status), headers: {}, config };
  if (status >= 400) {
    return Promise.reject(new AxiosError(`Request failed with status code ${status}`, undefined, config, undefined, response));
  }
  return Promise.resolve(response);
};

const adapterFor = (refresh: RefreshOutcome): AxiosAdapter => (config) => {
  if (config.url === '/api/auth/refresh') {
    if (refresh === 'network') {
      return Promise.reject(new AxiosError('Network Error', AxiosError.ERR_NETWORK, config));
    }
    if (refresh === 'server-error') return respond(config, 503, {});
    if (refresh === 'rejected') return respond(config, 401, {});
    return respond(config, 200, { token: 'fresh-token', user: { email: 'user@example.test' } });
  }

  const authorization = AxiosHeaders.from(config.headers).get('Authorization');
  return authorization === 'Bearer fresh-token'
    ? respond(config, 200, { ok: true })
    : respond(config, 401, {});
};

describe('apiClient session refresh', () => {
  const replace = vi.fn();
  const originalLocation = window.location;

  beforeEach(() => {
    localStorage.clear();
    AuthStorage.setItem(AUTH_TOKEN_KEY, 'expired-token');
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...originalLocation, pathname: '/app', search: '', hash: '', hostname: 'localhost', replace },
    });
  });

  afterEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
    apiClient.defaults.adapter = undefined;
    vi.clearAllMocks();
  });

  it('retries the request with the refreshed token', async () => {
    apiClient.defaults.adapter = adapterFor('ok');

    await expect(apiClient.get('/api/jobs')).resolves.toEqual({ ok: true });

    expect(AuthStorage.getItem(AUTH_TOKEN_KEY)).toBe('fresh-token');
    expect(replace).not.toHaveBeenCalled();
  });

  it.each<RefreshOutcome>(['network', 'server-error'])(
    'keeps the login when the refresh fails with %s',
    async (outcome) => {
      apiClient.defaults.adapter = adapterFor(outcome);

      await expect(apiClient.get('/api/jobs')).rejects.toBeInstanceOf(AxiosError);

      expect(AuthStorage.getItem(AUTH_TOKEN_KEY)).toBe('expired-token');
      expect(replace).not.toHaveBeenCalled();
      expect(notify.error).toHaveBeenCalledWith(REFRESH_UNAVAILABLE_MESSAGE);
    },
  );

  it('sends the user to sign in when the server rejects the refresh session', async () => {
    apiClient.defaults.adapter = adapterFor('rejected');

    await expect(apiClient.get('/api/jobs')).rejects.toBeInstanceOf(AxiosError);

    expect(AuthStorage.getItem(AUTH_TOKEN_KEY)).toBeNull();
    expect(replace).toHaveBeenCalledWith(expect.stringContaining('/login?reauth=1'));
  });
});
