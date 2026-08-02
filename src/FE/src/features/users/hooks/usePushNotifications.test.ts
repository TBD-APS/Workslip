import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getVapidPublicKey } from '../api/pushSubscriptions';
import { pushSubscriptionInternals } from './usePushNotifications';

vi.mock('../api/pushSubscriptions', () => ({
  getVapidPublicKey: vi.fn(),
  registerPushSubscription: vi.fn(),
}));

const applicationServerKey = new Uint8Array([1, 2, 3]);

function createSubscription(overrides: Partial<PushSubscription> = {}) {
  return {
    endpoint: 'https://push.example/current',
    expirationTime: null,
    options: {
      applicationServerKey,
      userVisibleOnly: true,
    },
    unsubscribe: vi.fn().mockResolvedValue(true),
    getKey: vi.fn((name: PushEncryptionKeyName) => {
      if (name === 'p256dh') return new Uint8Array([4, 5]).buffer;
      if (name === 'auth') return new Uint8Array([6, 7]).buffer;
      return null;
    }),
    toJSON: vi.fn(),
    ...overrides,
  } as unknown as PushSubscription;
}

function configureBrowser(
  existingSubscription: PushSubscription | null,
  subscribedSubscription = createSubscription({
    endpoint: 'https://push.example/new',
  }),
) {
  const subscribe = vi.fn().mockResolvedValue(subscribedSubscription);
  const pushManager = {
    getSubscription: vi.fn().mockResolvedValue(existingSubscription),
    subscribe,
  } as unknown as PushManager;

  Object.defineProperty(window, 'PushManager', {
    configurable: true,
    value: function PushManager() {},
  });
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: {
      ready: Promise.resolve({ pushManager }),
    },
  });

  return { subscribe };
}

function configurePermission(
  permission: NotificationPermission,
  requestResult: NotificationPermission = permission,
) {
  const requestPermission = vi.fn().mockResolvedValue(requestResult);
  Object.defineProperty(window, 'Notification', {
    configurable: true,
    value: {
      permission,
      requestPermission,
    },
  });
  return requestPermission;
}

describe('push subscription registration', () => {
  beforeEach(() => {
    vi.mocked(getVapidPublicKey).mockResolvedValue('AQID');
    configurePermission('granted');
  });

  it('returns null without fetching VAPID configuration when push is unsupported', async () => {
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: undefined,
    });
    Object.defineProperty(window, 'PushManager', {
      configurable: true,
      value: undefined,
    });

    await expect(pushSubscriptionInternals.ensurePushSubscription())
      .resolves.toBeNull();
    expect(getVapidPublicKey).not.toHaveBeenCalled();
  });

  it('returns null and does not subscribe when permission is denied', async () => {
    configurePermission('denied');
    const { subscribe } = configureBrowser(null);

    await expect(pushSubscriptionInternals.ensurePushSubscription())
      .resolves.toBeNull();
    expect(subscribe).not.toHaveBeenCalled();
  });

  it('requests permission when the browser state is default', async () => {
    const requestPermission = configurePermission('default', 'denied');
    const { subscribe } = configureBrowser(null);

    await expect(pushSubscriptionInternals.ensurePushSubscription())
      .resolves.toBeNull();
    expect(requestPermission).toHaveBeenCalledOnce();
    expect(subscribe).not.toHaveBeenCalled();
  });

  it('reuses a subscription created with the current VAPID key', async () => {
    const subscription = createSubscription();
    const { subscribe } = configureBrowser(subscription);

    await expect(pushSubscriptionInternals.ensurePushSubscription())
      .resolves.toEqual({
        endpoint: 'https://push.example/current',
        keys: {
          p256Dh: 'BAU=',
          auth: 'Bgc=',
        },
      });
    expect(subscription.unsubscribe).not.toHaveBeenCalled();
    expect(subscribe).not.toHaveBeenCalled();
  });

  it('replaces a stale VAPID subscription and reports its endpoint', async () => {
    const stale = createSubscription({
      endpoint: 'https://push.example/stale',
      options: {
        applicationServerKey: new Uint8Array([9, 9, 9]),
        userVisibleOnly: true,
      },
    });
    const replacement = createSubscription({
      endpoint: 'https://push.example/replacement',
    });
    const { subscribe } = configureBrowser(stale, replacement);

    await expect(pushSubscriptionInternals.ensurePushSubscription())
      .resolves.toEqual({
        endpoint: 'https://push.example/replacement',
        keys: {
          p256Dh: 'BAU=',
          auth: 'Bgc=',
        },
        replacedEndpoint: 'https://push.example/stale',
      });
    expect(stale.unsubscribe).toHaveBeenCalledOnce();
    expect(subscribe).toHaveBeenCalledWith({
      userVisibleOnly: true,
      applicationServerKey,
    });
  });

  it('fails when a stale subscription cannot be removed', async () => {
    const stale = createSubscription({
      options: {
        applicationServerKey: new Uint8Array([9]),
        userVisibleOnly: true,
      },
      unsubscribe: vi.fn().mockResolvedValue(false),
    });
    const { subscribe } = configureBrowser(stale);

    await expect(pushSubscriptionInternals.ensurePushSubscription())
      .rejects.toThrow('The stale push subscription could not be removed.');
    expect(subscribe).not.toHaveBeenCalled();
  });

  it.each(['p256dh', 'auth'] as const)(
    'fails when the browser omits the %s key',
    async (missingKey) => {
      const subscription = createSubscription({
        getKey: vi.fn((name: PushEncryptionKeyName) => {
          if (name === missingKey) return null;
          return new Uint8Array([1]).buffer;
        }),
      });
      configureBrowser(subscription);

      await expect(pushSubscriptionInternals.ensurePushSubscription())
        .rejects.toThrow('Could not retrieve keys from subscription object.');
    },
  );

  it('compares ArrayBuffer and typed-array VAPID keys by bytes', () => {
    expect(pushSubscriptionInternals.keysMatch(
      applicationServerKey.buffer,
      applicationServerKey,
    )).toBe(true);
    expect(pushSubscriptionInternals.keysMatch(
      new Uint8Array([1, 2, 4]),
      applicationServerKey,
    )).toBe(false);
    expect(pushSubscriptionInternals.keysMatch(null, applicationServerKey))
      .toBe(false);
  });

  it('decodes unpadded base64url VAPID keys', () => {
    expect([...pushSubscriptionInternals.urlBase64ToUint8Array('AQID-_8')])
      .toEqual([1, 2, 3, 251, 255]);
  });
});
