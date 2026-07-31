import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AUTH_TOKEN_KEY } from '../../../providers/authContextValue';
import {
  getVapidPublicKey,
  registerPushSubscription,
} from '../api/pushSubscriptions';
import {
  pushSubscriptionInternals,
  usePushNotifications,
} from './usePushNotifications';

vi.mock('../api/pushSubscriptions', () => ({
  getVapidPublicKey: vi.fn(),
  registerPushSubscription: vi.fn(),
}));

const currentPublicKey = 'AQID';

function createSubscription(
  endpoint: string,
  applicationServerKey: BufferSource,
  unsubscribe = vi.fn().mockResolvedValue(true),
): PushSubscription {
  return {
    endpoint,
    expirationTime: null,
    options: {
      applicationServerKey,
      userVisibleOnly: true,
    },
    getKey: (name: PushEncryptionKeyName) =>
      name === 'p256dh'
        ? Uint8Array.from([4, 5, 6]).buffer
        : Uint8Array.from([7, 8, 9]).buffer,
    toJSON: vi.fn(),
    unsubscribe,
  } as unknown as PushSubscription;
}

describe('push subscription registration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    vi.mocked(getVapidPublicKey).mockResolvedValue(currentPublicKey);
    vi.mocked(registerPushSubscription).mockResolvedValue(undefined);
    Object.defineProperty(window, 'PushManager', {
      configurable: true,
      value: class PushManager {},
    });
    Object.defineProperty(window, 'Notification', {
      configurable: true,
      value: {
        permission: 'granted',
        requestPermission: vi.fn(),
      },
    });
  });

  it('reuses a subscription created with the current VAPID key', async () => {
    const subscription = createSubscription(
      'https://push.example/current',
      Uint8Array.from([1, 2, 3]).buffer,
    );
    const subscribe = vi.fn();
    installServiceWorkerRegistration(subscription, subscribe);

    const payload = await pushSubscriptionInternals.ensurePushSubscription();

    expect(subscription.unsubscribe).not.toHaveBeenCalled();
    expect(subscribe).not.toHaveBeenCalled();
    expect(payload).toMatchObject({
      endpoint: 'https://push.example/current',
      keys: {
        p256Dh: 'BAUG',
        auth: 'BwgJ',
      },
    });
    expect(payload).not.toHaveProperty('replacedEndpoint');
  });

  it('replaces a subscription created with a previous VAPID key', async () => {
    const unsubscribe = vi.fn().mockResolvedValue(true);
    const staleSubscription = createSubscription(
      'https://push.example/stale',
      Uint8Array.from([9, 9, 9]).buffer,
      unsubscribe,
    );
    const newSubscription = createSubscription(
      'https://push.example/current',
      Uint8Array.from([1, 2, 3]).buffer,
    );
    const subscribe = vi.fn().mockResolvedValue(newSubscription);
    installServiceWorkerRegistration(staleSubscription, subscribe);

    const payload = await pushSubscriptionInternals.ensurePushSubscription();

    expect(unsubscribe).toHaveBeenCalledOnce();
    expect(subscribe).toHaveBeenCalledWith({
      userVisibleOnly: true,
      applicationServerKey: Uint8Array.from([1, 2, 3]),
    });
    expect(payload).toMatchObject({
      endpoint: 'https://push.example/current',
      replacedEndpoint: 'https://push.example/stale',
    });
  });

  it('registers the current device for a Superadmin session', async () => {
    localStorage.setItem(
      AUTH_TOKEN_KEY,
      `header.${btoa(JSON.stringify({ role: 'Superadmin' }))}.signature`,
    );
    const subscription = createSubscription(
      'https://push.example/superadmin',
      Uint8Array.from([1, 2, 3]).buffer,
    );
    installServiceWorkerRegistration(subscription, vi.fn());

    const queryClient = new QueryClient({
      defaultOptions: {
        mutations: { retry: false },
      },
    });
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children);
    const { result } = renderHook(() => usePushNotifications(), { wrapper });

    await act(async () => {
      await result.current.register();
    });

    expect(registerPushSubscription).toHaveBeenCalledWith({
      endpoint: 'https://push.example/superadmin',
      keys: {
        p256Dh: 'BAUG',
        auth: 'BwgJ',
      },
    });
  });
});

function installServiceWorkerRegistration(
  subscription: PushSubscription | null,
  subscribe: ReturnType<typeof vi.fn>,
) {
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: {
      ready: Promise.resolve({
        pushManager: {
          getSubscription: vi.fn().mockResolvedValue(subscription),
          subscribe,
        },
      }),
    },
  });
}
