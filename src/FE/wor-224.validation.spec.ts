import { test, expect, devices } from '@playwright/test';

const baseUrl = 'http://127.0.0.1:4173';

const adminUser = {
  id: '11111111-1111-1111-1111-111111111111',
  organizationId: '22222222-2222-2222-2222-222222222222',
  email: 'admin@example.test',
  displayName: 'Validation Admin',
  phone: '',
  role: 'Admin',
  roleDisplayName: 'Administrator',
  hoursThisWeek: null,
  hoursThisMonth: null,
  hoursBiweekly: null,
};

test.use({
  ...devices['Pixel 7'],
  serviceWorkers: 'block',
});

test('browser back closes an open drawer without leaving the current route', async ({ page }) => {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedApiRequests: string[] = [];

  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('requestfailed', (request) => {
    if (request.url().includes('/api/')) {
      failedApiRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`);
    }
  });

  await page.addInitScript(() => {
    const encode = (value: object) => btoa(JSON.stringify(value))
      .replace(/=/g, '')
      .replace(/\+/g, '-')
      .replace(/\//g, '_');
    const token = `${encode({ alg: 'none', typ: 'JWT' })}.${encode({
      role: 'Admin',
      exp: Math.floor(Date.now() / 1000) + 3600,
    })}.validation-signature`;

    localStorage.setItem('authToken', token);
    localStorage.setItem('userEmail', 'admin@example.test');

    try {
      delete (window as Window & { PushManager?: unknown }).PushManager;
    } catch {
      // The smoke does not exercise Web Push registration.
    }
  });

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.toLowerCase();

    if (path === '/api/auth/me') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(adminUser) });
      return;
    }

    if (path === '/api/jobs' || path === '/api/customers') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [], totalCount: 0 }),
      });
      return;
    }

    if (path === '/api/notifications') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
      return;
    }

    await route.fulfill({
      status: request.method() === 'GET' ? 200 : 204,
      contentType: 'application/json',
      body: request.method() === 'GET' ? '{}' : '',
    });
  });

  await page.goto(`${baseUrl}/app`);
  await expect(page.getByRole('heading', { name: 'Opgaver' })).toBeVisible();

  await page.getByRole('link', { name: 'Kunder' }).click();
  await expect(page).toHaveURL(`${baseUrl}/app/customers`);

  const notificationsButton = page.getByRole('button', { name: 'Notifikationer' });
  await notificationsButton.click();

  const drawer = page.getByRole('dialog', { name: 'Notifikationer' });
  await expect(drawer).toHaveClass(/\bopen\b/);

  await page.evaluate(() => window.history.back());

  await expect(drawer).not.toHaveClass(/\bopen\b/);
  await expect(page).toHaveURL(`${baseUrl}/app/customers`);
  await expect(page.getByRole('heading', { name: 'Log ind på Workslip' })).toHaveCount(0);

  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(failedApiRequests).toEqual([]);
});
