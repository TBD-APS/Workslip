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

const { chromium } = await import('playwright');
const browser = await chromium.launch({ headless: true });

const cases = [
  { name: 'day-desktop', theme: 'day', viewport: { width: 1280, height: 800 } },
  { name: 'night-mobile-390', theme: 'night', viewport: { width: 390, height: 844 } },
];

try {
  for (const testCase of cases) {
    await verifySharedStateSemantics(testCase);
  }
  console.log('[playwright] shared state action/selection/info semantics passed.');
} finally {
  await browser.close();
}

async function verifySharedStateSemantics({ name, theme, viewport }) {
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    viewport,
    colorScheme: theme === 'night' ? 'dark' : 'light',
  });
  await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: ADMIN_EMAIL,
  });
  await context.addInitScript(({ appOrigin, selectedTheme }) => {
    if (window.location.origin === appOrigin) localStorage.setItem('theme', selectedTheme);
  }, { appOrigin: new URL(APP_URL).origin, selectedTheme: theme });

  try {
    const page = await context.newPage();
    const pageErrors = [];
    const consoleErrors = [];
    page.on('pageerror', (error) => pageErrors.push(error.message));
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });

    const meResponse = page.waitForResponse((response) =>
      response.request().method() === 'GET' && new URL(response.url()).pathname === '/api/auth/me',
    { timeout: UI_TIMEOUT });
    const navigation = await page.goto(`${APP_URL}/app`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    assert.ok(navigation?.ok(), `${name}: authenticated navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);
    assert.equal((await meResponse).status(), 200, `${name}: /api/auth/me must succeed.`);
    await page.locator('#app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    if (viewport.width < 768) {
      // On phones the navigation lives in the hamburger drawer; open it first.
      await page.locator('#mobile-nav-toggle').click();
      const searchTrigger = page.locator('#bottom-nav-search');
      await searchTrigger.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await searchTrigger.click();
    } else {
      await page.keyboard.press('Control+K');
    }
    const dialog = page.locator('#quick-nav-dialog');
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const activeResult = dialog.locator('.quick-nav-result.active').first();
    await activeResult.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.waitForFunction(
      () => {
        const icon = document.querySelector('.quick-nav-result.active .quick-nav-result-icon');
        return icon instanceof HTMLElement && getComputedStyle(icon).color === 'rgb(20, 122, 126)';
      },
      null,
      { timeout: UI_TIMEOUT },
    );
    const activeNavigatorIcon = await activeResult.locator('.quick-nav-result-icon').evaluate(
      (element) => getComputedStyle(element).color,
    );

    // The linked-job focus contract lives outside Quick Navigator. Close the modal
    // before exercising it so the test does not fight the dialog's intentional
    // focus trap, then reach the target with a real keyboard Tab. Programmatic
    // focus alone does not reliably activate :focus-visible across Chromium input
    // modalities and made the release gate flaky on mobile-sized cases.
    await page.keyboard.press('Escape');
    await dialog.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });

    const result = await page.evaluate(() => {
      const shell = document.querySelector('.app-shell');
      if (!(shell instanceof HTMLElement)) {
        throw new Error('Expected app shell was not rendered.');
      }

      const probe = document.createElement('div');
      probe.setAttribute('data-shared-state-probe', '');
      probe.innerHTML = `
        <div class="activity-row activity-row-unread"><span class="activity-avatar activity-avatar-primary">A</span></div>
        <button type="button" data-focus-origin>Focus origin</button>
        <a class="linked-job-link">SAG-1</a>
        <button class="linked-job-row" type="button">Linked job</button>
        <span class="favorite-customer-icon">F</span>
        <span class="unread-dot"></span>
        <div class="worksheet-list-item is-selected"></div>
      `;
      shell.appendChild(probe);

      const bodyStyle = getComputedStyle(document.body);
      const activityRow = probe.querySelector('.activity-row-unread');
      const activityAvatar = probe.querySelector('.activity-avatar-primary');
      const linkedJob = probe.querySelector('.linked-job-link');
      const favorite = probe.querySelector('.favorite-customer-icon');
      const unread = probe.querySelector('.unread-dot');
      const worksheet = probe.querySelector('.worksheet-list-item.is-selected');
      if (!(activityRow instanceof HTMLElement)
        || !(activityAvatar instanceof HTMLElement)
        || !(linkedJob instanceof HTMLElement)
        || !(favorite instanceof HTMLElement)
        || !(unread instanceof HTMLElement)
        || !(worksheet instanceof HTMLElement)) {
        throw new Error('Shared-state CSS probe could not be constructed.');
      }

      return {
        primary: bodyStyle.getPropertyValue('--primary').trim(),
        colorPrimary: bodyStyle.getPropertyValue('--color-primary').trim(),
        colorInfo: bodyStyle.getPropertyValue('--color-info').trim(),
        focusRing: bodyStyle.getPropertyValue('--focus-ring').trim(),
        accentCoral: bodyStyle.getPropertyValue('--accent-coral').trim(),
        activityUnread: getComputedStyle(activityRow, '::before').backgroundColor,
        activityAvatar: getComputedStyle(activityAvatar).color,
        linkedJob: getComputedStyle(linkedJob).color,
        favorite: getComputedStyle(favorite).color,
        unreadDot: getComputedStyle(unread).backgroundColor,
        worksheetSelection: getComputedStyle(worksheet, '::before').backgroundColor,
        documentWidth: document.documentElement.scrollWidth,
        viewportWidth: window.innerWidth,
      };
    });

    await page.locator('[data-focus-origin]').focus();
    await page.keyboard.press('Tab');
    const focusResult = await page.evaluate(() => {
      const linkedJobRow = document.querySelector('[data-shared-state-probe] .linked-job-row');
      if (!(linkedJobRow instanceof HTMLElement)) {
        throw new Error('Linked-job focus target was not rendered.');
      }
      return {
        isActiveElement: document.activeElement === linkedJobRow,
        isFocusVisible: linkedJobRow.matches(':focus-visible'),
        outlineColor: getComputedStyle(linkedJobRow).outlineColor,
      };
    });

    assert.equal(result.primary, '#f47a24', `${name}: primary action token must remain signal orange.`);
    assert.equal(result.colorPrimary, '#147a7e', `${name}: selection/navigation token must remain petrol.`);
    assert.equal(result.colorInfo, '#147a7e', `${name}: informational token must remain petrol.`);
    assert.notEqual(result.colorPrimary, result.primary, `${name}: selection/navigation must stay distinct from primary action.`);
    assert.equal(activeNavigatorIcon, 'rgb(20, 122, 126)', `${name}: active Quick Navigator icon must use petrol.`);
    assert.equal(result.activityUnread, 'rgb(20, 122, 126)', `${name}: unread activity marker must be informational petrol.`);
    assert.equal(result.activityAvatar, 'rgb(20, 122, 126)', `${name}: activity actor/info avatar must be petrol.`);
    assert.equal(result.linkedJob, 'rgb(20, 122, 126)', `${name}: linked-job navigation must be petrol.`);
    assert.equal(focusResult.isActiveElement, true, `${name}: keyboard Tab must reach the linked-job row.`);
    assert.equal(focusResult.isFocusVisible, true, `${name}: linked-job keyboard focus must activate :focus-visible.`);
    assert.equal(
      focusResult.outlineColor,
      theme === 'day' ? 'rgb(20, 122, 126)' : 'rgb(85, 184, 184)',
      `${name}: linked-job focus must use the focus token, not action orange.`,
    );
    assert.equal(
      result.favorite,
      theme === 'day' ? 'rgb(200, 78, 69)' : 'rgb(239, 105, 95)',
      `${name}: favorite cue must use the semantic coral accent rather than a danger literal.`,
    );
    assert.equal(result.unreadDot, 'rgb(20, 122, 126)', `${name}: legacy unread dot must resolve to informational petrol.`);
    assert.equal(result.worksheetSelection, 'rgb(20, 122, 126)', `${name}: worksheet selected marker must resolve to petrol.`);
    assert.ok(result.documentWidth <= result.viewportWidth, `${name}: semantic styling must not introduce horizontal overflow.`);
    assert.deepEqual(pageErrors, [], `${name}: browser page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${name}: browser console errors: ${consoleErrors.join(' | ')}`);

    await page.evaluate(() => document.querySelector('[data-shared-state-probe]')?.remove());
  } finally {
    await context.close();
  }
}
