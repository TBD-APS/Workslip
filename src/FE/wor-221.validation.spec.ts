import { devices, expect, test, type Page } from '@playwright/test';

const baseUrl = 'http://127.0.0.1:4173';
const interruptedMessage = 'Login afbrudt. Klik på knappen for at prøve igen.';
const loginButtonName = 'Log ind med Microsoft passkey';

async function validateMicrosoftBackRecovery(page: Page) {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedRequests: string[] = [];

  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('requestfailed', (request) => {
    failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`);
  });

  await page.route('https://login.microsoftonline.com/**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'text/html; charset=utf-8',
      body: '<!doctype html><html><body><h1>Microsoft validation provider</h1></body></html>',
    });
  });

  await page.goto(`${baseUrl}/login`);
  const loginButton = page.getByRole('button', { name: loginButtonName, exact: true });
  await expect(loginButton).toBeVisible();
  await expect(loginButton).toBeEnabled();

  await loginButton.click();
  await page.waitForURL((url) => url.hostname === 'login.microsoftonline.com');
  await expect(page.getByRole('heading', { name: 'Microsoft validation provider' })).toBeVisible();
  const firstState = new URL(page.url()).searchParams.get('state');
  expect(firstState).toBeTruthy();

  await page.goBack({ waitUntil: 'domcontentloaded' });
  await expect(page).toHaveURL(`${baseUrl}/login`);
  await expect(page.getByText(interruptedMessage, { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: loginButtonName, exact: true })).toBeEnabled();
  await expect(page.getByText('Sender til Microsoft...', { exact: true })).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('workslip.loginPkce'))).toBeNull();

  await page.getByRole('button', { name: loginButtonName, exact: true }).click();
  await page.waitForURL((url) => url.hostname === 'login.microsoftonline.com');
  await expect(page.getByRole('heading', { name: 'Microsoft validation provider' })).toBeVisible();
  const secondState = new URL(page.url()).searchParams.get('state');
  expect(secondState).toBeTruthy();
  expect(secondState).not.toBe(firstState);

  await page.goBack({ waitUntil: 'domcontentloaded' });
  await expect(page.getByText(interruptedMessage, { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: loginButtonName, exact: true })).toBeEnabled();

  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(failedRequests).toEqual([]);
}

test.describe('WOR-221 desktop', () => {
  test.use({ viewport: { width: 1280, height: 900 } });

  test('recovers after browser back from Microsoft login', async ({ page }) => {
    await validateMicrosoftBackRecovery(page);
  });
});

test.describe('WOR-221 mobile', () => {
  const pixel7 = devices['Pixel 7'];
  test.use({
    viewport: pixel7.viewport,
    userAgent: pixel7.userAgent,
    deviceScaleFactor: pixel7.deviceScaleFactor,
    isMobile: pixel7.isMobile,
    hasTouch: pixel7.hasTouch,
  });

  test('recovers after browser back from Microsoft login', async ({ page }) => {
    await validateMicrosoftBackRecovery(page);
  });
});
