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
const REQUESTED_SCENARIO = String(process.env.WORKSLIP_PLAYWRIGHT_SCENARIO || 'all').trim().toLowerCase();
const UI_TIMEOUT = 25_000;
const APP_SHELL_OBSERVED_KEY = '__workslip_playwright_app_shell_observed';

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });

const scenarios = new Map([
  ['auth-session', verifyAuthSessionResilience],
  ['quick-navigator', verifyQuickNavigator],
]);

try {
  if (REQUESTED_SCENARIO === 'all') {
    for (const [name, scenario] of scenarios) {
      console.log(`[playwright] running ${name}.`);
      await scenario();
    }
  } else {
    const scenario = scenarios.get(REQUESTED_SCENARIO);
    if (!scenario) {
      throw new Error(`Unknown WORKSLIP_PLAYWRIGHT_SCENARIO '${REQUESTED_SCENARIO}'. Expected one of: ${[...scenarios.keys()].join(', ')}, all.`);
    }
    console.log(`[playwright] running ${REQUESTED_SCENARIO}.`);
    await scenario();
  }
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
  return { context, session };
}

async function openAuthenticatedApp(context, path = '/app') {
  const page = await context.newPage();
  const pageErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));

  const meResponse = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/auth/me',
  { timeout: UI_TIMEOUT });

  const navigation = await page.goto(`${APP_URL}${path}`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Authenticated app navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);

  const me = await meResponse;
  assert.equal(me.status(), 200, `/api/auth/me returned HTTP ${me.status()}.`);
  const user = await me.json();
  await page.locator('.app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  return {
    page,
    user,
    assertNoPageErrors() {
      assert.deepEqual(pageErrors, [], `Browser page errors: ${pageErrors.join(' | ')}`);
    },
  };
}

async function verifyAuthSessionResilience() {
  await verifyAuthenticatedBootstrapReloadAndLogout();
  await verifyMissingTokenFailsClosed();
  await verifyRejectedTokenFailsClosed();
}

async function verifyAuthenticatedBootstrapReloadAndLogout() {
  const { context, session: bootstrapSession } = await authenticatedContext({ viewport: { width: 1280, height: 800 } });
  try {
    const session = await openAuthenticatedApp(context, '/app/settings');
    const { page, user } = session;

    assert.equal(new URL(page.url()).pathname, '/app/settings', 'Direct protected-route navigation must preserve the requested route.');
    assert.equal(user.id, bootstrapSession.user.userId, '/api/auth/me user id must match the issued development identity.');
    assert.equal(user.organizationId, bootstrapSession.user.organizationId, '/api/auth/me tenant must match the issued development identity.');
    assert.equal(user.email.toLowerCase(), bootstrapSession.user.email.toLowerCase(), '/api/auth/me email must match the issued development identity.');
    assert.equal(user.role.toLowerCase(), bootstrapSession.user.role.toLowerCase(), '/api/auth/me role must match the issued development identity.');

    const storedSession = await page.evaluate(() => ({
      authToken: localStorage.getItem('authToken'),
      userEmail: localStorage.getItem('userEmail'),
    }));
    assert.ok(storedSession.authToken, 'Authenticated bootstrap must persist the bearer token.');
    assert.equal(storedSession.userEmail?.toLowerCase(), bootstrapSession.user.email.toLowerCase(), 'Authenticated bootstrap must persist the user email hint.');

    const reloadMeResponse = page.waitForResponse((response) =>
      response.request().method() === 'GET'
        && new URL(response.url()).pathname === '/api/auth/me',
    { timeout: UI_TIMEOUT });
    await page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    const reloadMe = await reloadMeResponse;
    assert.equal(reloadMe.status(), 200, `Reloaded /api/auth/me returned HTTP ${reloadMe.status()}.`);
    await page.locator('.app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(new URL(page.url()).pathname, '/app/settings', 'Reload must preserve the protected deep-link.');

    await page.getByRole('button', { name: 'Log ud' }).click();
    await page.waitForURL((url) => url.pathname === '/login', { timeout: UI_TIMEOUT });
    assert.equal(await page.locator('.app-shell').count(), 0, 'Explicit logout must remove the authenticated app shell.');
    const loggedOutStorage = await page.evaluate(() => ({
      authToken: localStorage.getItem('authToken'),
      userEmail: localStorage.getItem('userEmail'),
    }));
    assert.equal(loggedOutStorage.authToken, null, 'Explicit logout must clear the bearer token.');
    assert.equal(loggedOutStorage.userEmail, null, 'Explicit logout must clear the stored user email.');
    session.assertNoPageErrors();
  } finally {
    await context.close();
  }
}

async function observeAuthenticatedShell(context) {
  await context.addInitScript(({ observationKey }) => {
    if (sessionStorage.getItem(observationKey) === null) {
      sessionStorage.setItem(observationKey, '0');
    }

    const markIfAuthenticatedShellExists = () => {
      if (document.querySelector('.app-shell')) {
        sessionStorage.setItem(observationKey, '1');
      }
    };

    const startObserver = () => {
      markIfAuthenticatedShellExists();
      const observer = new MutationObserver(markIfAuthenticatedShellExists);
      observer.observe(document.documentElement, { childList: true, subtree: true });
    };

    if (document.documentElement) {
      startObserver();
    } else {
      window.addEventListener('DOMContentLoaded', startObserver, { once: true });
    }
  }, { observationKey: APP_SHELL_OBSERVED_KEY });
}

async function assertProtectedShellNeverRendered(page, message) {
  const observed = await page.evaluate((observationKey) => sessionStorage.getItem(observationKey), APP_SHELL_OBSERVED_KEY);
  assert.equal(observed, '0', message);
}

async function verifyMissingTokenFailsClosed() {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  await observeAuthenticatedShell(context);
  try {
    const page = await context.newPage();
    const pageErrors = [];
    page.on('pageerror', (error) => pageErrors.push(error.message));

    const navigation = await page.goto(`${APP_URL}/app/settings`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    assert.ok(navigation?.ok(), `Unauthenticated protected-route navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);
    await page.waitForURL((url) => url.pathname === '/login', { timeout: UI_TIMEOUT });
    assert.equal(new URL(page.url()).searchParams.get('returnTo'), '/app/settings', 'Missing-session redirect must preserve the requested protected route.');
    await assertProtectedShellNeverRendered(page, 'Protected app shell must never render when no session token exists.');
    assert.deepEqual(pageErrors, [], `Browser page errors during missing-token flow: ${pageErrors.join(' | ')}`);
  } finally {
    await context.close();
  }
}

async function verifyRejectedTokenFailsClosed() {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  await observeAuthenticatedShell(context);
  await context.addInitScript(({ email }) => {
    localStorage.setItem('authToken', 'invalid.playwright.session-token');
    localStorage.setItem('userEmail', email);
  }, { email: ADMIN_EMAIL });
  await context.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (['http:', 'https:'].includes(url.protocol) && !['127.0.0.1', 'localhost', '::1'].includes(url.hostname)) {
      await route.abort();
      return;
    }
    await route.continue();
  });

  try {
    const page = await context.newPage();
    const pageErrors = [];
    page.on('pageerror', (error) => pageErrors.push(error.message));

    const rejectedMeResponse = page.waitForResponse((response) =>
      response.request().method() === 'GET'
        && new URL(response.url()).pathname === '/api/auth/me',
    { timeout: UI_TIMEOUT });

    const navigation = await page.goto(`${APP_URL}/app/settings`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    assert.ok(navigation?.ok(), `Invalid-session protected-route navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);

    const rejectedMe = await rejectedMeResponse;
    assert.equal(rejectedMe.status(), 401, `Invalid session must be rejected by /api/auth/me; got HTTP ${rejectedMe.status()}.`);
    await page.waitForFunction(() => localStorage.getItem('authToken') === null, undefined, { timeout: UI_TIMEOUT });
    await page.waitForURL((url) => url.pathname === '/login', { timeout: UI_TIMEOUT });

    const rejectedStorage = await page.evaluate(() => ({
      authToken: localStorage.getItem('authToken'),
      userEmail: localStorage.getItem('userEmail'),
    }));
    assert.equal(rejectedStorage.authToken, null, 'Rejected session must clear the invalid bearer token.');
    assert.equal(rejectedStorage.userEmail?.toLowerCase(), ADMIN_EMAIL.toLowerCase(), 'Rejected session may retain only the verified email reauth hint.');
    await assertProtectedShellNeverRendered(page, 'Protected app shell must never render for a token rejected by /api/auth/me.');
    assert.deepEqual(pageErrors, [], `Browser page errors during rejected-token flow: ${pageErrors.join(' | ')}`);
  } finally {
    await context.close();
  }
}

async function verifyQuickNavigator() {
  await verifyMobileQuickNavigator();
  await verifyDesktopQuickNavigator();
}

async function verifyMobileQuickNavigator() {
  const { context } = await authenticatedContext(devices['iPhone 13']);
  try {
    const session = await openAuthenticatedApp(context);
    const { page } = session;
    await page.locator('.quick-nav-mobile-trigger').click();
    const dialog = page.getByRole('dialog', { name: 'Hvor vil du hen?' });
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    assert.equal(await dialog.locator('.quick-nav-search-wrap kbd').isVisible(), false, 'Esc key hint must be hidden on mobile.');
    assert.equal(await dialog.locator('.quick-nav-footer').isVisible(), false, 'Keyboard shortcut footer must be hidden on mobile.');
    session.assertNoPageErrors();
  } finally {
    await context.close();
  }
}

async function verifyDesktopQuickNavigator() {
  const { context } = await authenticatedContext({ viewport: { width: 1280, height: 800 } });
  try {
    const session = await openAuthenticatedApp(context);
    const { page } = session;
    await page.keyboard.press('Control+K');
    const dialog = page.getByRole('dialog', { name: 'Hvor vil du hen?' });
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    assert.equal(await dialog.locator('.quick-nav-search-wrap kbd').isVisible(), true, 'Esc key hint must remain visible on desktop.');
    assert.equal(await dialog.locator('.quick-nav-footer').isVisible(), true, 'Keyboard shortcut footer must remain visible on desktop.');
    session.assertNoPageErrors();
  } finally {
    await context.close();
  }
}
