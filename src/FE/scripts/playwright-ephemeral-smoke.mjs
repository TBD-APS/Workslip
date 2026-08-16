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

try {
  await verifyMobileQuickNavigator();
  await verifyDesktopQuickNavigator();
  console.log('[playwright] authenticated ephemeral browser smoke passed.');
} finally {
  await browser.close();
}

async function authenticatedContext(contextOptions) {
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    ...contextOptions,
  });
  const session = await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: ADMIN_EMAIL,
  });
  assert.equal(String(session.user.role).toLowerCase(), 'admin', 'Synthetic browser identity must resolve to Admin.');
  return context;
}

async function openAuthenticatedApp(context) {
  const page = await context.newPage();
  const pageErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));

  const meResponse = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/auth/me',
  { timeout: UI_TIMEOUT });

  const navigation = await page.goto(`${APP_URL}/app`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Authenticated app navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);

  const me = await meResponse;
  assert.equal(me.status(), 200, `/api/auth/me returned HTTP ${me.status()}.`);
  await page.locator('.app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  assert.deepEqual(pageErrors, [], `Browser page errors: ${pageErrors.join(' | ')}`);
  return page;
}

async function verifyMobileQuickNavigator() {
  const context = await authenticatedContext(devices['iPhone 13']);
  try {
    const page = await openAuthenticatedApp(context);
    await page.locator('.quick-nav-mobile-trigger').click();
    const dialog = page.getByRole('dialog', { name: 'Hvor vil du hen?' });
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    assert.equal(await dialog.locator('.quick-nav-search-wrap kbd').isVisible(), false, 'Esc key hint must be hidden on mobile.');
    assert.equal(await dialog.locator('.quick-nav-footer').isVisible(), false, 'Keyboard shortcut footer must be hidden on mobile.');
  } finally {
    await context.close();
  }
}

async function verifyDesktopQuickNavigator() {
  const context = await authenticatedContext({ viewport: { width: 1280, height: 800 } });
  try {
    const page = await openAuthenticatedApp(context);
    await page.locator('.quick-nav-header-trigger').click();
    const dialog = page.getByRole('dialog', { name: 'Hvor vil du hen?' });
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    assert.equal(await dialog.locator('.quick-nav-search-wrap kbd').isVisible(), true, 'Esc key hint must remain visible on desktop.');
    assert.equal(await dialog.locator('.quick-nav-footer').isVisible(), true, 'Keyboard shortcut footer must remain visible on desktop.');
  } finally {
    await context.close();
  }
}
