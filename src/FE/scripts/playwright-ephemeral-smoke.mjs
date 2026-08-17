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
const LARGE_UPLOAD_TIMEOUT = 60_000;
const APP_SHELL_OBSERVED_KEY = '__workslip_playwright_app_shell_observed';
const INVALID_TOKEN_SEEDED_KEY = '__workslip_playwright_invalid_token_seeded';

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });

const scenarios = new Map([
  ['auth-session', verifyAuthSessionResilience],
  ['quick-navigator', verifyQuickNavigator],
  ['document-upload', verifyMobileDocumentAttachmentUpload],
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

    const accountMenuButton = page.getByRole('button', { name: 'Profil og konto' });
    await accountMenuButton.click();
    const accountMenu = page.getByRole('menu', { name: 'Profil og konto' });
    await accountMenu.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await accountMenu.getByRole('menuitem', { name: 'Log ud' }).click();
    await page.waitForURL((url) => url.pathname === '/login', {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
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
  const deadline = Date.now() + UI_TIMEOUT;
  while (true) {
    try {
      const observed = await page.evaluate((observationKey) => sessionStorage.getItem(observationKey), APP_SHELL_OBSERVED_KEY);
      assert.equal(observed, '0', message);
      return;
    } catch (error) {
      const navigationRace = String(error?.message || error).includes('Execution context was destroyed');
      if (!navigationRace || Date.now() >= deadline) {
        throw error;
      }
      await page.waitForLoadState('domcontentloaded', {
        timeout: Math.max(1, deadline - Date.now()),
      }).catch(() => {});
    }
  }
}

async function readOriginLocalStorage(context, origin) {
  const state = await context.storageState();
  const entries = state.origins.find((item) => item.origin === origin)?.localStorage ?? [];
  return Object.fromEntries(entries.map(({ name, value }) => [name, value]));
}

async function waitForOriginLocalStorage(context, origin, predicate) {
  const deadline = Date.now() + UI_TIMEOUT;
  while (true) {
    const storage = await readOriginLocalStorage(context, origin);
    if (predicate(storage)) {
      return storage;
    }
    if (Date.now() >= deadline) {
      return storage;
    }
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
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
    await page.waitForURL((url) => url.pathname === '/login', {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
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
  await context.addInitScript(({ email, appOrigin, seedKey }) => {
    if (window.location.origin !== appOrigin || sessionStorage.getItem(seedKey) === '1') {
      return;
    }
    localStorage.setItem('authToken', 'invalid.playwright.session-token');
    localStorage.setItem('userEmail', email);
    sessionStorage.setItem(seedKey, '1');
  }, {
    email: ADMIN_EMAIL,
    appOrigin: new URL(APP_URL).origin,
    seedKey: INVALID_TOKEN_SEEDED_KEY,
  });
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
    await page.waitForURL((url) => url.pathname === '/login', {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });

    const rejectedStorage = await waitForOriginLocalStorage(
      context,
      new URL(APP_URL).origin,
      (storage) => storage.authToken === undefined
        && storage.userEmail?.toLowerCase() === ADMIN_EMAIL.toLowerCase(),
    );
    assert.equal(rejectedStorage.authToken ?? null, null, 'Rejected session must clear the invalid bearer token.');
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

async function assertGlobalSearchSurface(page, dialog) {
  await dialog.getByText('Global søgning', { exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await dialog.getByText('Søg på tværs af funktioner, sager og kunder fra ét sted.', { exact: true })
    .waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  const searchInput = dialog.getByRole('searchbox', { name: 'Søg i hele Workslip' });
  const searchWrap = dialog.locator('.quick-nav-search-wrap');
  await searchInput.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  await dialog.getByRole('button', { name: 'Luk søgning' }).focus();
  const unfocusedBorder = await searchWrap.evaluate((element) => getComputedStyle(element).borderTopColor);
  await searchInput.focus();
  const focusedStyles = await searchWrap.evaluate((element) => {
    const style = getComputedStyle(element);
    return { border: style.borderTopColor, boxShadow: style.boxShadow };
  });
  assert.equal(focusedStyles.border, unfocusedBorder, 'Search focus must not replace the neutral border with a petrol/green border.');
  assert.notEqual(focusedStyles.boxShadow, 'none', 'Search focus must retain a visible neutral focus affordance.');

  return searchInput;
}

async function verifyMobileQuickNavigator() {
  const { context } = await authenticatedContext(devices['iPhone 13']);
  try {
    const session = await openAuthenticatedApp(context);
    const { page } = session;
    const consoleErrors = [];
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });

    await page.locator('.quick-nav-mobile-trigger').click();
    const dialog = page.getByRole('dialog', { name: 'Søg i hele Workslip' });
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await assertGlobalSearchSurface(page, dialog);

    assert.equal(await dialog.locator('.quick-nav-search-wrap kbd').isVisible(), false, 'Esc key hint must be hidden on mobile.');
    assert.equal(await dialog.locator('.quick-nav-footer').isVisible(), false, 'Keyboard shortcut footer must be hidden on mobile.');
    assert.deepEqual(consoleErrors, [], `Quick Navigator mobile console errors: ${consoleErrors.join(' | ')}`);
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
    const consoleErrors = [];
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });

    await page.keyboard.press('Control+K');
    const dialog = page.getByRole('dialog', { name: 'Søg i hele Workslip' });
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const searchInput = await assertGlobalSearchSurface(page, dialog);

    assert.equal(await dialog.locator('.quick-nav-search-wrap kbd').isVisible(), true, 'Esc key hint must remain visible on desktop.');
    assert.equal(await dialog.locator('.quick-nav-footer').isVisible(), true, 'Keyboard shortcut footer must remain visible on desktop.');

    const jobRequest = page.waitForRequest((request) => {
      const url = new URL(request.url());
      return request.method() === 'GET'
        && url.pathname === '/api/jobs'
        && url.searchParams.get('search') === 'Niels';
    }, { timeout: UI_TIMEOUT });
    const customerRequest = page.waitForRequest((request) => {
      const url = new URL(request.url());
      return request.method() === 'GET'
        && url.pathname === '/api/customers/search'
        && url.searchParams.get('query') === 'Niels';
    }, { timeout: UI_TIMEOUT });

    await searchInput.fill('Niels');
    await Promise.all([jobRequest, customerRequest]);
    assert.deepEqual(consoleErrors, [], `Quick Navigator desktop console errors: ${consoleErrors.join(' | ')}`);
    session.assertNoPageErrors();
  } finally {
    await context.close();
  }
}

async function createExact75MbMp3Fixture() {
  const [{ mkdtemp, open, rm, stat }, { tmpdir }, { join }] = await Promise.all([
    import('node:fs/promises'),
    import('node:os'),
    import('node:path'),
  ]);
  const directory = await mkdtemp(join(tmpdir(), 'workslip-playwright-upload-'));
  const filePath = join(directory, 'boundary-75mb.mp3');
  const expectedBytes = 75 * 1024 * 1024;
  const file = await open(filePath, 'w');
  try {
    await file.write(Buffer.from([0x49, 0x44, 0x33, 0x04]), 0, 4, 0);
    await file.truncate(expectedBytes);
  } finally {
    await file.close();
  }

  const metadata = await stat(filePath);
  assert.equal(metadata.size, expectedBytes, 'Large upload fixture must be exactly 75 MB.');
  return {
    filePath,
    cleanup: () => rm(directory, { recursive: true, force: true }),
  };
}

async function verifyMobileDocumentAttachmentUpload() {
  const { context } = await authenticatedContext(devices['iPhone 13']);
  let fixture;
  try {
    const session = await openAuthenticatedApp(context);
    const { page } = session;

    await page.goto(`${APP_URL}/app/docs/new`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.waitForURL((url) => url.pathname === '/app/docs/new', { timeout: UI_TIMEOUT });
    await page.getByLabel('Titel').fill(`Upload cap ${Date.now()}`);

    const createResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'POST'
        && new URL(response.url()).pathname === '/api/docs',
    { timeout: UI_TIMEOUT });
    await page.getByRole('button', { name: 'Gem', exact: true }).click();
    const createResponse = await createResponsePromise;
    assert.ok(createResponse.ok(), `Document create returned HTTP ${createResponse.status()}.`);
    const document = await createResponse.json();
    assert.ok(document?.id, 'Created document response did not contain an id.');
    await page.waitForURL((url) => url.pathname === `/app/docs/${document.id}`, { timeout: UI_TIMEOUT });

    const input = page.locator('input.docs-file-input[type="file"]');
    await input.waitFor({ state: 'attached', timeout: UI_TIMEOUT });
    fixture = await createExact75MbMp3Fixture();

    const [uploadResponse] = await Promise.all([
      page.waitForResponse((response) =>
        response.request().method() === 'POST'
          && new URL(response.url()).pathname === `/api/docs/${document.id}/attachments`,
      { timeout: LARGE_UPLOAD_TIMEOUT }),
      input.setInputFiles(fixture.filePath, { timeout: LARGE_UPLOAD_TIMEOUT }),
    ]);
    assert.ok(uploadResponse.ok(), `75 MB document attachment returned HTTP ${uploadResponse.status()}.`);

    await page.getByText('Filen er tilføjet.', { exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.getByText('boundary-75mb.mp3', { exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.match(await page.locator('.docs-attachments-help').innerText(), /maks\. 75 MB pr\. fil/);
    session.assertNoPageErrors();
  } finally {
    await fixture?.cleanup();
    await context.close();
  }
}
