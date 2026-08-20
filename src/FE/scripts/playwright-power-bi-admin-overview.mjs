import assert from 'node:assert/strict';
import process from 'node:process';
import { requireLoopbackOrigin, seedLocalBrowserSession } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270',
  'WORKSLIP_PLAYWRIGHT_APP_URL',
);
const API_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_API_URL || 'http://127.0.0.1:5262',
  'WORKSLIP_PLAYWRIGHT_API_URL',
);
const ADMIN_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL || 'admin@17v3ygzs.mailosaur.net').trim();
const USER_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_USER_EMAIL || 'user@17v3ygzs.mailosaur.net').trim();
const UI_TIMEOUT = 25_000;

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });

async function assertNoBrowserErrors(page, label) {
  const pageErrors = [];
  const consoleErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  return () => {
    assert.deepEqual(pageErrors, [], `${label} page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${label} console errors: ${consoleErrors.join(' | ')}`);
  };
}

async function exerciseAdmin(contextOptions, label) {
  const context = await browser.newContext({
    ...contextOptions,
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: ADMIN_EMAIL,
  });

  const page = await context.newPage();
  const verifyErrors = await assertNoBrowserErrors(page, label);

  const overviewResponse = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/power-bi/overview/job-status',
  { timeout: UI_TIMEOUT });

  const navigation = await page.goto(`${APP_URL}/app/overblik`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `${label} Overview returned HTTP ${navigation?.status() ?? 'unknown'}.`);
  assert.equal((await overviewResponse).status(), 200, `${label} Power BI summary endpoint must return 200 for Admin.`);
  await page.getByTestId('admin-power-bi-job-status').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.getByRole('heading', { name: 'Sagsfordeling' }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  await page.goto(`${APP_URL}/app/timer`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.waitForLoadState('networkidle', { timeout: UI_TIMEOUT });
  assert.equal(await page.getByText(/Power BI/i).count(), 0, `${label} Timer must not expose Power BI UI.`);

  verifyErrors();
  await context.close();
}

async function exerciseNormalUser() {
  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: USER_EMAIL,
  });

  const page = await context.newPage();
  const verifyErrors = await assertNoBrowserErrors(page, 'normal-user');
  let powerBiSummaryRequests = 0;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/power-bi/overview/job-status') {
      powerBiSummaryRequests += 1;
    }
  });

  const navigation = await page.goto(`${APP_URL}/app/overblik`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Normal-user Overview returned HTTP ${navigation?.status() ?? 'unknown'}.`);
  await page.getByRole('heading', { name: 'Overblik' }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.waitForTimeout(500);
  assert.equal(await page.getByTestId('admin-power-bi-job-status').count(), 0, 'Normal user must not render the Power BI chart.');
  assert.equal(powerBiSummaryRequests, 0, 'Normal user must not request the Power BI summary endpoint.');

  verifyErrors();
  await context.close();
}

try {
  await exerciseAdmin({ viewport: { width: 1280, height: 800 } }, 'admin-desktop');
  await exerciseAdmin(devices['iPhone 13'], 'admin-mobile');
  await exerciseNormalUser();
  console.log('[playwright] Power BI Admin Overview + Timer isolation passed on desktop/mobile.');
} finally {
  await browser.close();
}
