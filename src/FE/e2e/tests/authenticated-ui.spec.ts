import type { Route } from '@playwright/test';
import { expect, test } from '../support/test';

const userId = '11111111-1111-4111-8111-111111111111';
const organizationId = '22222222-2222-4222-8222-222222222222';

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

test.describe('@authenticated-ui authenticated application shell', () => {
  test('navigates to the create form, verifies its initial state, and logs out', async ({ page }) => {
    await page.addInitScript(({ email }) => {
      localStorage.setItem('authToken', 'playwright-ui-token');
      localStorage.setItem('userEmail', email);
      localStorage.removeItem('workslip.reauthInFlight');
    }, { email: 'playwright-admin@example.test' });

    await page.route('**/api/**', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      const path = url.pathname.replace(/\/+$/, '');
      const method = request.method();

      // Vite source modules can contain `/api/` in their file path, for example
      // `/src/features/auth/api/entraLogin.ts`. Only intercept real server routes.
      if (!url.pathname.startsWith('/api/')) {
        await route.continue();
        return;
      }

      if (method === 'GET' && path === '/api/auth/me') {
        await fulfillJson(route, {
          id: userId,
          organizationId,
          email: 'playwright-admin@example.test',
          displayName: 'Playwright Admin',
          phone: '00000000',
          role: 'Admin',
          roleDisplayName: 'Administrator',
          hoursThisWeek: 0,
          hoursThisMonth: 0,
          hoursBiweekly: 0,
        });
        return;
      }

      if (method === 'GET' && path === '/api/jobs') {
        await fulfillJson(route, { items: [], totalCount: 0 });
        return;
      }

      if (method === 'GET' && path === '/api/users') {
        await fulfillJson(route, { users: [], total: 0 });
        return;
      }

      if (method === 'GET' && path === '/api/reference-data') {
        await fulfillJson(route, {
          installationTypes: [],
          workKinds: [],
          closureFlags: [],
        });
        return;
      }

      if (method === 'GET' && path === '/api/notifications') {
        await fulfillJson(route, { items: [], totalCount: 0 });
        return;
      }

      if (method === 'POST' && path === '/api/push-subscriptions') {
        await route.fulfill({ status: 204, body: '' });
        return;
      }

      await fulfillJson(route, {
        error: 'unmocked_playwright_request',
        method,
        path,
      }, 501);
    });

    await page.goto('/app');

    await expect(page).toHaveURL(/\/app\/?$/);
    await expect(page.getByRole('link', { name: 'Sager' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Opret ny sag' })).toBeVisible();

    await page.getByRole('button', { name: 'Opret ny sag' }).click();
    await expect(page.getByRole('heading', { name: 'Opret' })).toBeVisible();
    await page.getByRole('button', { name: /^Diverse job/ }).click();

    await expect(page).toHaveURL(/\/app\/job\/simple\/new$/);
    await expect(page.getByRole('heading', { name: 'Simpelt job' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Opret job' })).toBeDisabled();

    await page.getByRole('button', { name: 'Tilbage' }).first().click();
    await expect(page).toHaveURL(/\/app\/?$/);

    await page.getByRole('button', { name: 'Log ud' }).click();
    await expect(page).toHaveURL(/\/login(?:\?|$)/);
    await expect(page.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toBeVisible();
  });
});
