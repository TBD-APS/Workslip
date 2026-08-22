import assert from 'node:assert/strict';
import process from 'node:process';
import { requireLoopbackOrigin } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270',
  'WORKSLIP_PLAYWRIGHT_APP_URL',
);
const UI_TIMEOUT = 25_000;

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });

const cases = [
  { name: 'desktop-1280', context: { viewport: { width: 1280, height: 800 } } },
  { name: 'mobile-iPhone-13', context: devices['iPhone 13'] },
];

try {
  for (const testCase of cases) {
    await verifyHelpWizard(testCase);
  }
  console.log('[playwright] help wizard flag + interaction evidence passed on desktop and mobile.');
} finally {
  await browser.close();
}

async function verifyHelpWizard({ name, context: contextOptions }) {
  const context = await browser.newContext({
    ...contextOptions,
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });

  try {
    const page = await context.newPage();
    const pageErrors = [];
    const consoleErrors = [];
    page.on('pageerror', (error) => pageErrors.push(error.message));
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });

    const navigation = await page.goto(`${APP_URL}/login`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    assert.ok(navigation?.ok(), `${name}: /login returned HTTP ${navigation?.status() ?? 'unknown'}.`);
    await page.locator('#login-card').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await page.locator('#help-wizard').count(), 0, `${name}: help wizard must fail closed without an assignment.`);

    await page.evaluate(() => localStorage.setItem('workslip.flag.help-wizard', 'on'));
    await page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });

    const toggle = page.locator('#help-wizard-toggle');
    await toggle.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false', `${name}: help wizard must start collapsed.`);
    assert.equal(await page.locator('#help-wizard-message').count(), 0, `${name}: collapsed help wizard must not render its message.`);

    await toggle.click();
    const message = page.locator('#help-wizard-message');
    await message.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await toggle.getAttribute('aria-expanded'), 'true', `${name}: help wizard toggle must expose its open state.`);

    const bounds = await page.locator('#help-wizard').boundingBox();
    assert.ok(bounds, `${name}: help wizard must have visible bounds.`);
    assert.ok(bounds.x >= 0, `${name}: help wizard must not overflow the left viewport edge.`);
    assert.ok(bounds.x + bounds.width <= page.viewportSize().width + 0.5, `${name}: help wizard must not overflow the right viewport edge.`);
    assert.equal(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      true,
      `${name}: help wizard must not introduce horizontal page overflow.`,
    );

    await toggle.click();
    await message.waitFor({ state: 'detached', timeout: UI_TIMEOUT });
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false', `${name}: second activation must collapse the help wizard.`);
    assert.deepEqual(pageErrors, [], `${name}: browser page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${name}: browser console errors: ${consoleErrors.join(' | ')}`);
  } finally {
    await context.close();
  }
}
