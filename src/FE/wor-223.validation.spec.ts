import { expect, test } from '@playwright/test';

declare global {
  interface Window {
    __pushValidation: {
      unsubscribedEndpoint: string | null;
      subscribedKey: number[] | null;
    };
    runPushSubscriptionRepair: () => Promise<{
      endpoint: string;
      replacedEndpoint?: string;
    } | null>;
  }
}

const scenarios = [
  { name: 'desktop Chromium', viewport: { width: 1280, height: 800 } },
  { name: 'Pixel 7 Chromium', viewport: { width: 412, height: 915 } },
];

for (const scenario of scenarios) {
  test(`${scenario.name} replaces a stale VAPID subscription`, async ({ page }) => {
    await page.setViewportSize(scenario.viewport);
    await page.route('**/api/push-subscriptions/public-key', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ publicKey: 'AQID' }),
      });
    });

    await page.addInitScript(() => {
      const state = {
        unsubscribedEndpoint: null as string | null,
        subscribedKey: null as number[] | null,
      };

      const createSubscription = (
        endpoint: string,
        applicationServerKey: ArrayBuffer,
        onUnsubscribe?: () => void,
      ) => ({
        endpoint,
        expirationTime: null,
        options: {
          applicationServerKey,
          userVisibleOnly: true,
        },
        getKey: (name: string) =>
          name === 'p256dh'
            ? Uint8Array.from([4, 5, 6]).buffer
            : Uint8Array.from([7, 8, 9]).buffer,
        toJSON: () => ({}),
        unsubscribe: async () => {
          onUnsubscribe?.();
          return true;
        },
      });

      const staleEndpoint = 'https://push.example/stale';
      const staleSubscription = createSubscription(
        staleEndpoint,
        Uint8Array.from([9, 9, 9]).buffer,
        () => {
          state.unsubscribedEndpoint = staleEndpoint;
        },
      );

      const pushManager = {
        getSubscription: async () => staleSubscription,
        subscribe: async (options: PushSubscriptionOptionsInit) => {
          const key = options.applicationServerKey;
          const bytes = key instanceof ArrayBuffer
            ? new Uint8Array(key)
            : new Uint8Array(key!.buffer, key!.byteOffset, key!.byteLength);
          state.subscribedKey = Array.from(bytes);
          return createSubscription(
            'https://push.example/current',
            Uint8Array.from(bytes).buffer,
          );
        },
      };

      Object.defineProperty(window, 'PushManager', {
        configurable: true,
        value: class PushManager {},
      });
      Object.defineProperty(window, 'Notification', {
        configurable: true,
        value: {
          permission: 'granted',
          requestPermission: async () => 'granted',
        },
      });
      Object.defineProperty(navigator, 'serviceWorker', {
        configurable: true,
        value: {
          ready: Promise.resolve({ pushManager }),
        },
      });
      window.__pushValidation = state;
    });

    await page.goto('http://127.0.0.1:4174/wor-223.validation.html');
    await page.waitForFunction(() => typeof window.runPushSubscriptionRepair === 'function');

    const result = await page.evaluate(() => window.runPushSubscriptionRepair());
    const state = await page.evaluate(() => window.__pushValidation);

    expect(result).toMatchObject({
      endpoint: 'https://push.example/current',
      replacedEndpoint: 'https://push.example/stale',
    });
    expect(state).toEqual({
      unsubscribedEndpoint: 'https://push.example/stale',
      subscribedKey: [1, 2, 3],
    });
  });
}
