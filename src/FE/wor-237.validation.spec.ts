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
  channel: 'chrome',
  viewport: { width: 390, height: 844 },
  userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 Chrome/149.0.0.0 Mobile Safari/537.36',
});

test('mobile Superadmin can enter, delegate, and exit an organization', async ({ page }) => {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedRequests: string[] = [];
  const authMeBearerTokens: string[] = [];
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
      const authorization = request.headers().authorization ?? '';
      authMeBearerTokens.push(authorization);
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

  const organizationButton = page.getByRole('button', { name: /NP Teknik CVR/ });

  await expect(page).toHaveURL(/\/superadmin$/);
  await expect(page.getByRole('heading', { name: 'Superadmin' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Superadmin' })).toBeVisible();
  await expect(organizationButton).toBeVisible();
  await expect(page.getByText('Superadmin er kun tilgængelig på computer')).toHaveCount(0);
  expect(organizationListRequests).toBeGreaterThan(0);

  await organizationButton.click();
  await page.getByRole('button', { name: 'Åbn organisation' }).click();

  await expect.poll(() => sessionRequests).toBe(1);
  await page.waitForURL(/\/app$/, { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('load');

  await expect.poll(async () => {
    try {
      return await page.evaluate((keys) => ({
        activeToken: localStorage.getItem('authToken'),
        homeToken: localStorage.getItem(keys.homeToken),
        organizationId: localStorage.getItem(keys.organizationId),
        organizationName: localStorage.getItem(keys.organizationName),
      }), {
        homeToken: HOME_AUTH_TOKEN_KEY,
        organizationId: ORGANIZATION_SESSION_ID_KEY,
        organizationName: ORGANIZATION_SESSION_NAME_KEY,
      });
    } catch {
      return null;
    }
  }).toEqual({
    activeToken: delegatedToken,
    homeToken,
    organizationId: CUSTOMER_ORGANIZATION_ID,
    organizationName: 'NP Teknik',
  });
  await expect.poll(() => authMeBearerTokens.includes(`Bearer ${delegatedToken}`)).toBe(true);

  const sessionBanner = page.locator('.organization-session-banner');
  const exitSessionButton = page.getByRole('button', { name: 'Afslut organisationssession' });

  await expect(sessionBanner).toContainText('NP Teknik');
  await expect(exitSessionButton).toBeVisible();

  await exitSessionButton.click();
  await page.waitForURL(/\/superadmin$/, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: 'Superadmin' })).toBeVisible();

  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(failedRequests).toEqual([]);
});
