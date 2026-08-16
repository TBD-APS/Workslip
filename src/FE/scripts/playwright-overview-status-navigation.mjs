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
const UI_TIMEOUT = 25_000;

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });

const destinations = [
  { card: 'Aktive sager', status: 'Draft', filter: 'Aktiv' },
  { card: 'Til gennemsyn', status: 'InReview', filter: 'Til gennemsyn' },
  { card: 'Godkendte sager', status: 'Approved', filter: 'Godkendt' },
];

try {
  const context = await browser.newContext({
    ...devices['iPhone 13'],
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });

  await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: ADMIN_EMAIL,
  });

  const page = await context.newPage();
  const pageErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));

  const meResponse = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/auth/me',
  { timeout: UI_TIMEOUT });

  const navigation = await page.goto(`${APP_URL}/app/overblik`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Overview navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);
  assert.equal((await meResponse).status(), 200, 'Authenticated Overview bootstrap must resolve /api/auth/me with 200.');
  await page.getByRole('heading', { name: 'Overblik' }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  for (const destination of destinations) {
    const statusRegion = page.getByRole('region', { name: 'Sagsstatus' });
    await statusRegion.getByRole('button', { name: new RegExp(destination.card, 'i') }).click();
    await page.waitForURL((url) =>
      url.pathname === '/app' && url.searchParams.get('status') === destination.status,
    { timeout: UI_TIMEOUT });

    const selectedFilter = page.getByRole('button', { name: destination.filter, exact: true });
    await selectedFilter.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(
      await selectedFilter.getAttribute('aria-pressed'),
      'true',
      `${destination.card} must visibly select the ${destination.filter} filter.`,
    );

    await page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.waitForURL((url) =>
      url.pathname === '/app' && url.searchParams.get('status') === destination.status,
    { timeout: UI_TIMEOUT });
    const reloadedFilter = page.getByRole('button', { name: destination.filter, exact: true });
    await reloadedFilter.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(
      await reloadedFilter.getAttribute('aria-pressed'),
      'true',
      `Reload must preserve the ${destination.filter} deep-linked filter.`,
    );

    await page.goBack({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.waitForURL((url) => url.pathname === '/app/overblik', { timeout: UI_TIMEOUT });
    await page.getByRole('heading', { name: 'Overblik' }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  assert.deepEqual(pageErrors, [], `Browser page errors: ${pageErrors.join(' | ')}`);
  await context.close();
  console.log('[playwright] mobile Overview status navigation passed.');
} finally {
  await browser.close();
}
