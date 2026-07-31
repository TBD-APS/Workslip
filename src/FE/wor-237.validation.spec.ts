import { expect, test } from '@playwright/test';

const ACTOR_ID = '11111111-1111-4111-8111-111111111111';
const HOME_ORGANIZATION_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const CUSTOMER_ORGANIZATION_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

function token(payload: Record<string, unknown>) {
  return `header.${Buffer.from(JSON.stringify(payload)).toString('base64url')}.signature`;
}

const homeToken = token({
  nameid: ACTOR_ID,
  organizationId: HOME_ORGANIZATION_ID,
  role: 'Superadmin',
  exp: Math.floor(Date.now() / 1000) + 3600,
});

const delegatedToken = token({
  nameid: ACTOR_ID,
  organizationId: CUSTOMER_ORGANIZATION_ID,
  homeOrganizationId: HOME_ORGANIZATION_ID,
  role: 'Superadmin',
  exp: Math.floor(Date.now() / 1000) + 900,
  delegatedOrganizationSession: true,
});

test.use({
  viewport: { width: 390, height: 844 },
  userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 Chrome/149.0.0.0 Mobile Safari/537.36',
});

test('mobile Superadmin can enter, delegate, and exit an organization', async ({ page }) => {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedRequests: string[] = [];
  let organizationListRequests = 0;
  let sessionRequests = 0;

  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('requestfailed', (request) => {
    failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`);
  });

  await page.addInitScript((storedToken) => {
    localStorage.setItem('authToken', storedToken);
  }, homeToken);

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (path === '/api/auth/me') {
      const delegated = request.headers().authorization?.includes(delegatedToken) ?? false;
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
      organizationListRequests += 1;
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

    if (path.startsWith('/api/jobs')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
  });

  await page.goto('http://127.0.0.1:4173/app');

  await expect(page).toHaveURL(/\/superadmin$/);
  await expect(page.getByRole('heading', { name: 'Superadmin' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Superadmin' })).toBeVisible();
  await expect(page.getByText('NP Teknik', { exact: true })).toBeVisible();
  await expect(page.getByText('Superadmin er kun tilgængelig på computer')).toHaveCount(0);
  expect(organizationListRequests).toBeGreaterThan(0);

  await page.getByRole('button', { name: /NP Teknik/ }).click();
  await page.getByRole('button', { name: 'Åbn organisation' }).click();

  await expect(page).toHaveURL(/\/app$/);
  await expect(page.getByText(/Du arbejder i/)).toContainText('NP Teknik');
  expect(sessionRequests).toBe(1);

  await page.getByRole('button', { name: 'Afslut organisationssession' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);
  await expect(page.getByRole('heading', { name: 'Superadmin' })).toBeVisible();

  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(failedRequests).toEqual([]);
});
