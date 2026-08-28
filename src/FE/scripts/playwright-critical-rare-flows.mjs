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
const UI_TIMEOUT = 25_000;

const IDENTITIES = {
  admin: {
    email: String(process.env.WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL || 'admin@17v3ygzs.mailosaur.net').trim(),
    role: 'admin',
  },
  user: {
    email: String(process.env.WORKSLIP_PLAYWRIGHT_USER_EMAIL || 'user@17v3ygzs.mailosaur.net').trim(),
    role: 'user',
  },
  auditor: {
    email: String(process.env.WORKSLIP_PLAYWRIGHT_AUDITOR_EMAIL || 'auditor@17v3ygzs.mailosaur.net').trim(),
    role: 'auditor',
  },
};

const { chromium } = await import('playwright');
const browser = await chromium.launch({ headless: true });

try {
  console.log('[playwright] rare flow: transient startup recovery.');
  await verifyTransientStartupRecovery();

  console.log('[playwright] rare flow: user permission boundaries.');
  await verifyUserPermissionBoundaries();

  console.log('[playwright] rare flow: auditor permission boundaries.');
  await verifyAuditorPermissionBoundaries();

  console.log('[playwright] rare authenticated flows passed.');
} finally {
  await browser.close();
}

async function createAuthenticatedContext(identity, viewport = { width: 1280, height: 800 }) {
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    viewport,
  });
  const session = await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: identity.email,
  });
  assert.equal(
    String(session.user.role).toLowerCase(),
    identity.role,
    `Synthetic ${identity.role} identity resolved to an unexpected role.`,
  );
  return { context, session };
}

function observeBrowserErrors(page) {
  const pageErrors = [];
  const consoleErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  return { pageErrors, consoleErrors };
}

async function openAuthenticatedPage(context, path) {
  const page = await context.newPage();
  const errors = observeBrowserErrors(page);
  const meResponse = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/auth/me',
  { timeout: UI_TIMEOUT });

  const navigation = await page.goto(`${APP_URL}${path}`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Navigation to ${path} returned HTTP ${navigation?.status() ?? 'unknown'}.`);
  const me = await meResponse;
  assert.equal(me.status(), 200, `${path}: /api/auth/me returned HTTP ${me.status()}.`);
  await page.locator('#app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  return { page, ...errors };
}

async function expectRoute(page, path) {
  await page.waitForURL((url) => url.pathname === path, { timeout: UI_TIMEOUT });
  assert.equal(new URL(page.url()).pathname, path);
  await page.locator('#app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
}

async function navigateAndExpect(page, requestedPath, expectedPath) {
  const navigation = await page.goto(`${APP_URL}${requestedPath}`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Navigation to ${requestedPath} returned HTTP ${navigation?.status() ?? 'unknown'}.`);
  await expectRoute(page, expectedPath);
}

async function verifyTransientStartupRecovery() {
  const { context } = await createAuthenticatedContext(IDENTITIES.admin);
  try {
    const page = await context.newPage();
    const { pageErrors, consoleErrors } = observeBrowserErrors(page);
    let backendAvailable = false;

    await page.route('**/api/auth/me', async (route) => {
      if (!backendAvailable) {
        await route.fulfill({
          status: 503,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'synthetic startup warmup' }),
        });
        return;
      }
      await route.continue();
    });

    const navigation = await page.goto(`${APP_URL}/app/settings`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    assert.ok(navigation?.ok(), `Startup-recovery navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);

    const recoveryTitle = page.locator('#fullscreen-system-state-title');
    await recoveryTitle.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.waitForFunction(
      () => document.querySelector('#fullscreen-system-state-title')?.textContent?.trim()
        === 'Forbindelsen tager længere tid end normalt',
      undefined,
      { timeout: UI_TIMEOUT },
    );
    assert.equal((await recoveryTitle.textContent())?.trim(), 'Forbindelsen tager længere tid end normalt');
    assert.equal(await page.locator('#app-shell').count(), 0, 'Authenticated shell must not render while session verification is unavailable.');

    const beforeRetry = await page.evaluate(() => ({
      token: localStorage.getItem('authToken'),
      email: localStorage.getItem('userEmail'),
    }));
    assert.ok(beforeRetry.token, 'Transient startup failure must preserve the valid auth token.');
    assert.equal(beforeRetry.email?.toLowerCase(), IDENTITIES.admin.email.toLowerCase(), 'Transient startup failure must preserve the login hint.');

    backendAvailable = true;
    const recoveredMe = page.waitForResponse((response) =>
      response.request().method() === 'GET'
        && new URL(response.url()).pathname === '/api/auth/me'
        && response.status() === 200,
    { timeout: UI_TIMEOUT });
    await page.locator('#startup-retry-button').click();
    await recoveredMe;
    await page.locator('#app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expectRoute(page, '/app/settings');

    const afterRetry = await page.evaluate(() => ({
      token: localStorage.getItem('authToken'),
      email: localStorage.getItem('userEmail'),
    }));
    assert.equal(afterRetry.token, beforeRetry.token, 'Successful retry must reuse the preserved valid session instead of replacing it.');
    assert.equal(afterRetry.email, beforeRetry.email, 'Successful retry must preserve the login hint.');
    assert.deepEqual(pageErrors, [], `Startup recovery page errors: ${pageErrors.join(' | ')}`);

    const unexpectedConsoleErrors = consoleErrors.filter((message) => !/503|service unavailable|synthetic startup warmup/i.test(message));
    assert.deepEqual(
      unexpectedConsoleErrors,
      [],
      `Unexpected startup recovery console errors: ${unexpectedConsoleErrors.join(' | ')}`,
    );
  } finally {
    await context.close();
  }
}

async function verifyUserPermissionBoundaries() {
  const { context } = await createAuthenticatedContext(IDENTITIES.user);
  try {
    const { page, pageErrors, consoleErrors } = await openAuthenticatedPage(context, '/app/customers');
    await expectRoute(page, '/app/customers');

    await page.locator('#bottom-nav-customers').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const docsNavigation = page.locator('#bottom-nav-docs');
    assert.equal(await docsNavigation.count(), 1, 'User navigation must expose the permitted Docs destination.');
    await docsNavigation.scrollIntoViewIfNeeded();
    await docsNavigation.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await page.locator('#bottom-nav-people').count(), 0, 'User navigation must not expose user management.');

    await page.locator('#account-menu-button').click();
    const menu = page.locator('#account-menu');
    await menu.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await page.locator('#account-menu-settings').count(), 0, 'User account menu must not expose admin settings.');
    assert.equal(await page.locator('#account-menu-docs').count(), 0, 'Docs belongs in the primary navigation, not the account menu.');
    await page.keyboard.press('Escape');

    await navigateAndExpect(page, '/app/timer', '/app/timer');
    await navigateAndExpect(page, '/app/settings', '/app');
    await navigateAndExpect(page, '/app/users', '/app');
    await navigateAndExpect(page, '/app/auditor', '/app');
    await navigateAndExpect(page, '/app/customers/new', '/app');

    assert.deepEqual(pageErrors, [], `User permission-flow page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `User permission-flow console errors: ${consoleErrors.join(' | ')}`);
  } finally {
    await context.close();
  }
}

async function verifyAuditorPermissionBoundaries() {
  const { context } = await createAuthenticatedContext(IDENTITIES.auditor);
  try {
    const { page, pageErrors, consoleErrors } = await openAuthenticatedPage(context, '/app/auditor');
    await expectRoute(page, '/app/auditor');

    await page.locator('#bottom-nav-home').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await page.locator('#bottom-nav-timer').count(), 0, 'Auditor navigation must not expose worksheets.');
    assert.equal(await page.locator('#bottom-nav-people').count(), 0, 'Auditor navigation must not expose user management.');
    assert.equal(await page.locator('#bottom-nav-customers').count(), 0, 'Auditor navigation must not expose customers.');
    assert.equal(await page.locator('#app-notifications-button').count(), 0, 'Auditor shell must not expose notifications.');

    await page.locator('#account-menu-button').click();
    const menu = page.locator('#account-menu');
    await menu.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await page.locator('#account-menu-settings').count(), 0, 'Auditor account menu must not expose admin settings.');
    await page.locator('#account-menu-docs').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.keyboard.press('Escape');

    await navigateAndExpect(page, '/app/timer', '/app/auditor');
    await navigateAndExpect(page, '/app/settings', '/app/auditor');
    await navigateAndExpect(page, '/app/users', '/app/auditor');
    await navigateAndExpect(page, '/app/customers', '/app/auditor');
    await navigateAndExpect(page, '/app/job/new', '/app/auditor');
    await navigateAndExpect(page, '/app/docs', '/app/docs');

    assert.deepEqual(pageErrors, [], `Auditor permission-flow page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `Auditor permission-flow console errors: ${consoleErrors.join(' | ')}`);
  } finally {
    await context.close();
  }
}
