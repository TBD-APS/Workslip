import assert from 'node:assert/strict';
import process from 'node:process';
import { requireLoopbackOrigin } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270',
  'WORKSLIP_PLAYWRIGHT_APP_URL',
);
const UI_TIMEOUT = 25_000;
const MAX_CLIPPY_COPY_LENGTH = 120;

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
  console.log('[playwright] Clippy 2.0 gold mascot + movement evidence passed on desktop and mobile.');
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

    // The production default is fail-closed. Exercise the enabled path with an
    // explicit identity assignment, then verify the identity-off path below.
    await page.evaluate(() => localStorage.setItem('workslip.flag.help-wizard', 'on'));
    await page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.locator('#login-card').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const wizard = page.locator('#help-wizard');
    const toggle = page.locator('#help-wizard-toggle');
    await toggle.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false', `${name}: Clippy 2.0 must start collapsed.`);
    assert.equal(await page.locator('#help-wizard-message').count(), 0, `${name}: collapsed Clippy 2.0 must stay quiet.`);
    assert.equal(await page.locator('#help-wizard-character').count(), 1, `${name}: Clippy must render the gold paperclip identity.`);
    assert.equal(await page.locator('#help-wizard-wand').count(), 1, `${name}: gold Clippy must keep the magic wand.`);

    const homeBounds = await wizard.boundingBox();
    const viewport = page.viewportSize();
    assert.ok(homeBounds && viewport, `${name}: Clippy 2.0 must have visible bounds.`);
    assert.ok(homeBounds.x >= 0, `${name}: Clippy 2.0 must not overflow the left viewport edge.`);
    assert.ok(homeBounds.x < viewport.width / 3, `${name}: Clippy 2.0 must start on the left side of the UI.`);
    assert.ok(homeBounds.y + homeBounds.height <= viewport.height + 0.5, `${name}: Clippy 2.0 must not overflow the bottom viewport edge.`);
    assert.equal(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      true,
      `${name}: Clippy 2.0 must not introduce horizontal page overflow.`,
    );

    await page.evaluate(() => {
      const target = document.createElement('button');
      target.id = 'clippy-motion-target';
      target.type = 'button';
      target.textContent = 'Target';
      Object.assign(target.style, {
        position: 'fixed',
        right: '24px',
        top: '120px',
        width: '112px',
        height: '40px',
        zIndex: '-1',
      });
      document.body.appendChild(target);
      window.dispatchEvent(new CustomEvent('workslip:clippy-command', {
        detail: { type: 'go-to', targetId: 'clippy-motion-target' },
      }));
    });

    await page.waitForFunction(
      ({ homeX }) => {
        const element = document.getElementById('help-wizard');
        if (!element || element.dataset.clippyMode !== 'target') return false;
        return element.getBoundingClientRect().left > homeX + 80;
      },
      { homeX: homeBounds.x },
      { timeout: UI_TIMEOUT },
    );

    const targetBounds = await wizard.boundingBox();
    assert.ok(targetBounds, `${name}: Clippy 2.0 must remain visible at a target.`);
    assert.ok(targetBounds.x >= 0, `${name}: moving Clippy must stay inside the left viewport edge.`);
    assert.ok(targetBounds.x + targetBounds.width <= viewport.width + 0.5, `${name}: moving Clippy must stay inside the right viewport edge.`);
    assert.ok(targetBounds.y >= 0, `${name}: moving Clippy must stay inside the top viewport edge.`);
    assert.ok(targetBounds.y + targetBounds.height <= viewport.height + 0.5, `${name}: moving Clippy must stay inside the bottom viewport edge.`);

    await page.evaluate(() => window.dispatchEvent(new CustomEvent('workslip:clippy-command', {
      detail: { type: 'point-at', targetId: 'clippy-motion-target' },
    })));
    await page.waitForFunction(
      () => document.getElementById('help-wizard')?.dataset.clippyReaction === 'attention',
      null,
      { timeout: UI_TIMEOUT },
    );

    await page.evaluate(() => window.dispatchEvent(new CustomEvent('workslip:clippy-command', {
      detail: { type: 'go-home' },
    })));
    await page.waitForFunction(
      ({ homeX, homeY }) => {
        const element = document.getElementById('help-wizard');
        if (!element || element.dataset.clippyMode !== 'home') return false;
        const rect = element.getBoundingClientRect();
        return Math.abs(rect.left - homeX) < 3 && Math.abs(rect.top - homeY) < 3;
      },
      { homeX: homeBounds.x, homeY: homeBounds.y },
      { timeout: UI_TIMEOUT },
    );

    await toggle.click();
    const message = page.locator('#help-wizard-message');
    await message.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const copy = (await message.textContent())?.trim() ?? '';
    assert.ok(copy.length > 0, `${name}: Clippy must show useful copy only after the user opens it.`);
    assert.ok(copy.length <= MAX_CLIPPY_COPY_LENGTH, `${name}: Clippy copy must stay concise.`);
    assert.equal(await page.locator('#help-wizard-message-title').count(), 1, `${name}: Clippy copy must have a scannable headline.`);
    assert.equal(await page.locator('#help-wizard-message-body').count(), 1, `${name}: Clippy copy must keep the explanation bounded.`);
    assert.equal(await toggle.getAttribute('aria-expanded'), 'true', `${name}: Clippy 2.0 must expose its open state.`);

    await toggle.click();
    await message.waitFor({ state: 'detached', timeout: UI_TIMEOUT });

    await page.evaluate(() => localStorage.setItem('workslip.flag.help-wizard', 'off'));
    await page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    assert.equal(await page.locator('#help-wizard').count(), 0, `${name}: explicit identity off must hide Clippy 2.0.`);

    await page.evaluate(() => localStorage.removeItem('workslip.flag.help-wizard'));
    await page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.locator('#help-wizard-toggle').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    assert.deepEqual(pageErrors, [], `${name}: browser page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${name}: browser console errors: ${consoleErrors.join(' | ')}`);
  } finally {
    await context.close();
  }
}
