import { expect, test } from '@playwright/test';

const ACTOR_ID = '11111111-1111-4111-8111-111111111111';
const HOME_ORGANIZATION_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const CUSTOMER_ORGANIZATION_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const HOME_AUTH_TOKEN_KEY = 'workslip.superadmin.homeAuthToken';
const ORGANIZATION_SESSION_ID_KEY = 'workslip.superadmin.organizationSessionId';
const ORGANIZATION_SESSION_NAME_KEY = 'workslip.superadmin.organizationSessionName';

function token(payload: Record<string, unknown>) {
  return `header.${Buffer.from(JSON.stringify(payload)).toString('base64url')}.signature`;
}

const homeToken = token({
  nameid: ACTOR_ID,
  workslipUserId: ACTOR_ID,
  organizationId: HOME_ORGANIZATION_ID,
  role: 'Superadmin',
  exp: Math.floor(Date.now() / 1000) + 3600,
});

const delegatedToken = token({
  nameid: ACTOR_ID,
  workslipUserId: ACTOR_ID,
  organizationId: CUSTOMER_ORGANIZATION_ID,
  homeOrganizationId: HOME_ORGANIZATION_ID,
  role: 'Superadmin',
  exp: Math.floor(Date.now() / 1000) + 900,
  delegatedOrganizationSession: true,
});

test.use({
  channel: 'chrome',
  serviceWorkers: 'block',
  viewport: { width: 390, height: 844 },
  userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 Chrome/149.0.0.0 Mobile Safari/537.36',
});

test('Superadmin registers push and uses notification UI before and during delegation', async ({ page }) => {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedRequests: string[] = [];
  const registrationRequests: Array<{ authorization: string; body: unknown }> = [];
  let sessionRequests = 0;

  page.on('console', (message) => {
    if (message.type() !== 'error') return;
    const text = message.text();
    if (text.startsWith('[PWA] Registration failed:')) return;
    consoleErrors.push(text);
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('requestfailed', (request) => {
    failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`);
  });

  await page.addInitScript((storedToken) => {
    if (!localStorage.getItem('authToken')) {
      localStorage.setItem('authToken', storedToken);
    }

    const applicationServerKey = Uint8Array.from([1, 2, 3]).buffer;
    const subscription = {
      endpoint: 'https://push.example/superadmin-device',
      expirationTime: null,
      options: { applicationServerKey, userVisibleOnly: true },
      getKey(name: string) {
        return name === 'p256dh'
          ? Uint8Array.from([4, 5, 6]).buffer
          : Uint8Array.from([7, 8, 9]).buffer;
      },
      toJSON() { return {}; },
      async unsubscribe() { return true; },
    };
    const registration = {
      active: null,
      installing: null,
      waiting: null,
      pushManager: {
        async getSubscription() { return subscription; },
        async subscribe() { return subscription; },
      },
      addEventListener() {},
      removeEventListener() {},
      async update() {},
    };
    const serviceWorkerContainer = {
      controller: null,
      ready: Promise.resolve(registration),
      async register() { return registration; },
      async getRegistration() { return registration; },
      async getRegistrations() { return [registration]; },
      addEventListener() {},
      removeEventListener() {},
    };

    Object.defineProperty(window, 'PushManager', {
      configurable: true,
      value: class PushManager {},
    });
    Object.defineProperty(window, 'Notification', {
      configurable: true,
      value: class Notification {
        static permission = 'granted';
        static async requestPermission() { return 'granted'; }
      },
    });
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: serviceWorkerContainer,
    });
  }, homeToken);

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace(/\/$/, '');
    const authorization = request.headers().authorization ?? '';

    if (path === '/api/auth/me') {
      const delegated = authorization === `Bearer ${delegatedToken}`;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: ACTOR_ID,
          organizationId: delegated ? CUSTOMER_ORGANIZATION_ID : HOME_ORGANIZATION_ID,
          email: 'superadmin@workslip.dk',
          displayName: 'Super Admin',
          phone: '',
          role: 'Superadmin',
          roleDisplayName: 'Superadministrator',
          hoursThisWeek: null,
          hoursThisMonth: null,
          hoursBiweekly: null,
        }),
      });
      return;
    }

    if (path === '/api/organizations' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik', cvr: '12345678' },
        ]),
      });
      return;
    }

    if (
      path === `/api/organizations/${CUSTOMER_ORGANIZATION_ID}/session`
      && request.method() === 'POST'
    ) {
      sessionRequests += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          token: delegatedToken,
          tokenType: 'Bearer',
          expiresIn: 900,
          user: {
            userId: ACTOR_ID,
            organizationId: CUSTOMER_ORGANIZATION_ID,
            email: 'superadmin@workslip.dk',
            displayName: 'Super Admin',
            role: 'Superadmin',
          },
        }),
      });
      return;
    }

    if (path === '/api/push-subscriptions/public-key') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ publicKey: 'AQID' }),
      });
      return;
    }

    if (path === '/api/push-subscriptions' && request.method() === 'POST') {
      registrationRequests.push({
        authorization,
        body: request.postDataJSON(),
      });
      await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
      return;
    }

    if (path === '/api/notifications' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
            title: 'Ny sag',
            body: 'Du har modtaget en ny sag.',
            url: null,
            createdUtc: '2026-07-31T15:00:00Z',
            isRead: false,
            status: 'Completed',
          },
        ]),
      });
      return;
    }

    if (path === '/api/jobs' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [], totalCount: 0 }),
      });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
  });

  await page.goto('http://127.0.0.1:4173/app');
  await expect(page).toHaveURL(/\/superadmin$/);
  await expect(page.getByRole('heading', { name: 'Superadmin' })).toBeVisible();

  await expect.poll(() => registrationRequests.length).toBeGreaterThanOrEqual(1);
  expect(registrationRequests[0]).toEqual({
    authorization: `Bearer ${homeToken}`,
    body: {
      endpoint: 'https://push.example/superadmin-device',
      keys: { p256Dh: 'BAUG', auth: 'BwgJ' },
    },
  });

  const homeBell = page.getByRole('button', { name: /Notifikationer/ });
  await expect(homeBell).toBeVisible();
  await homeBell.click();
  const homeDrawer = page.getByRole('dialog', { name: 'Notifikationer' });
  await expect(homeDrawer).toBeVisible();
  await expect(homeDrawer.getByText('Ny sag')).toBeVisible();
  await page.getByRole('button', { name: 'Tilbage fra notifikationer' }).click();
  await expect(homeDrawer).not.toHaveClass(/open/);

  await page.getByRole('button', { name: /NP Teknik CVR/ }).click();
  await page.getByRole('button', { name: 'Åbn organisation' }).click();
  await expect.poll(() => sessionRequests).toBe(1);
  await page.waitForURL(/\/app$/, { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('load');

  await expect.poll(async () => page.evaluate((keys) => ({
    activeToken: localStorage.getItem('authToken'),
    homeToken: localStorage.getItem(keys.homeToken),
    organizationId: localStorage.getItem(keys.organizationId),
    organizationName: localStorage.getItem(keys.organizationName),
  }), {
    homeToken: HOME_AUTH_TOKEN_KEY,
    organizationId: ORGANIZATION_SESSION_ID_KEY,
    organizationName: ORGANIZATION_SESSION_NAME_KEY,
  })).toEqual({
    activeToken: delegatedToken,
    homeToken,
    organizationId: CUSTOMER_ORGANIZATION_ID,
    organizationName: 'NP Teknik',
  });

  await expect.poll(() => registrationRequests.some(
    (entry) => entry.authorization === `Bearer ${delegatedToken}`,
  )).toBe(true);
  await expect(page.locator('.organization-session-banner')).toContainText('NP Teknik');

  const delegatedBell = page.getByRole('button', { name: /Notifikationer/ });
  await expect(delegatedBell).toBeVisible();
  await delegatedBell.click();
  const delegatedDrawer = page.getByRole('dialog', { name: 'Notifikationer' });
  await expect(delegatedDrawer).toBeVisible();
  await expect(delegatedDrawer.getByText('Ny sag')).toBeVisible();

  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(failedRequests).toEqual([]);
});
