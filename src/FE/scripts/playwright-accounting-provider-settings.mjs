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
const ADMIN_EMAIL = String(
  process.env.WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL || 'admin@17v3ygzs.mailosaur.net',
).trim();
const UI_TIMEOUT = 25_000;

const { chromium } = await import('playwright');
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({
  locale: 'da-DK',
  timezoneId: 'Europe/Copenhagen',
  viewport: { width: 1280, height: 800 },
});

try {
  const session = await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: ADMIN_EMAIL,
  });
  assert.equal(String(session.user.role).toLowerCase(), 'admin');

  const page = await context.newPage();
  const pageErrors = [];
  const consoleErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });

  const initialSettingsResponse = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/settings/accounting',
  { timeout: UI_TIMEOUT });

  const navigation = await page.goto(`${APP_URL}/app/settings`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Admin settings navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);

  const initialResponse = await initialSettingsResponse;
  assert.equal(initialResponse.status(), 200, `Accounting settings GET returned HTTP ${initialResponse.status()}.`);

  const selector = page.locator('#accounting-provider-selector');
  const saveButton = page.locator('#accounting-provider-save');
  await selector.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await saveButton.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  const economicsOption = selector.locator('option[value="economics"]');
  assert.equal(await economicsOption.count(), 1, 'e-conomic is not available in the accounting provider selector.');
  assert.equal((await economicsOption.textContent())?.trim(), 'e-conomic');

  const originalProviderId = await selector.inputValue();
  const targetProviderId = originalProviderId === 'economics' ? '' : 'economics';
  let changed = false;

  try {
    await selector.selectOption(targetProviderId);
    assert.equal(await saveButton.isEnabled(), true, 'Save must enable after changing the accounting provider.');

    const saveResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'PUT'
        && new URL(response.url()).pathname === '/api/settings/accounting',
    { timeout: UI_TIMEOUT });
    await saveButton.click();
    const saveResponse = await saveResponsePromise;
    assert.equal(saveResponse.status(), 204, `Accounting settings PUT returned HTTP ${saveResponse.status()}.`);
    changed = true;

    await page.waitForFunction(
      (expected) => document.querySelector('#accounting-provider-selector')?.value === expected,
      targetProviderId,
      { timeout: UI_TIMEOUT },
    );
    assert.equal(await saveButton.isDisabled(), true, 'Save must disable after the provider selection is persisted.');
  } finally {
    if (changed) {
      await selector.selectOption(originalProviderId);
      const restoreResponsePromise = page.waitForResponse((response) =>
        response.request().method() === 'PUT'
          && new URL(response.url()).pathname === '/api/settings/accounting',
      { timeout: UI_TIMEOUT });
      await saveButton.click();
      const restoreResponse = await restoreResponsePromise;
      assert.equal(restoreResponse.status(), 204, `Accounting settings restore PUT returned HTTP ${restoreResponse.status()}.`);
    }
  }

  assert.deepEqual(pageErrors, [], `Accounting provider page errors: ${pageErrors.join(' | ')}`);
  assert.deepEqual(consoleErrors, [], `Accounting provider console errors: ${consoleErrors.join(' | ')}`);
  console.log('[playwright] Admin accounting provider selector passed.');
} finally {
  await context.close();
  await browser.close();
}
