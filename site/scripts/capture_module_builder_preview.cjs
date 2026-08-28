const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');

const baseUrl = process.env.SITE_PREVIEW_URL || 'http://127.0.0.1:4000/';
const outputDir = 'site-preview-artifacts';

fs.mkdirSync(outputDir, { recursive: true });

const attachErrorCollection = (page, label) => {
  const errors = [];
  page.on('pageerror', (error) => errors.push(`${label} pageerror: ${error.message}`));
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(`${label} console: ${message.text()}`);
  });
  return errors;
};

const assertCount = async (page, expected) => {
  const count = await page.locator('[data-module-count]').textContent();
  assert.equal(count?.trim(), String(expected), `Expected ${expected} enabled modules, got ${count}`);
};

(async () => {
  const browser = await chromium.launch();

  try {
    const desktop = await browser.newPage({
      viewport: { width: 1440, height: 1000 },
      deviceScaleFactor: 1
    });
    const desktopErrors = attachErrorCollection(desktop, 'desktop');

    await desktop.goto(baseUrl, { waitUntil: 'networkidle' });
    const desktopBuilder = desktop.locator('[data-module-builder]');
    await desktopBuilder.waitFor({ state: 'visible' });
    await desktopBuilder.scrollIntoViewIfNeeded();
    await assertCount(desktop, 0);

    await desktopBuilder.screenshot({
      path: `${outputDir}/desktop-core.png`,
      animations: 'disabled'
    });

    const core = desktop.locator('[data-module-core]');
    await desktop.locator('[data-module="kls"]').dragTo(core);
    await assertCount(desktop, 1);
    await desktop.locator('[data-module="inventory"]').dragTo(core);
    await assertCount(desktop, 2);
    await desktop.locator('[data-module="insights"]').dragTo(core);
    await assertCount(desktop, 3);

    assert.equal(await core.getAttribute('data-level'), '3');
    assert.equal(await desktop.locator('[data-module="kls"]').getAttribute('aria-pressed'), 'true');
    assert.equal(await desktop.locator('[data-module="inventory"]').getAttribute('aria-pressed'), 'true');
    assert.equal(await desktop.locator('[data-module="insights"]').getAttribute('aria-pressed'), 'true');

    await desktopBuilder.screenshot({
      path: `${outputDir}/desktop-enriched.png`,
      animations: 'disabled'
    });

    assert.deepEqual(desktopErrors, [], `Browser errors detected:\n${desktopErrors.join('\n')}`);
    await desktop.close();

    const mobile = await browser.newPage({
      viewport: { width: 390, height: 844 },
      deviceScaleFactor: 1,
      isMobile: true,
      hasTouch: true
    });
    const mobileErrors = attachErrorCollection(mobile, 'mobile');

    await mobile.goto(baseUrl, { waitUntil: 'networkidle' });
    const mobileBuilder = mobile.locator('[data-module-builder]');
    await mobileBuilder.waitFor({ state: 'visible' });
    await mobile.locator('[data-module="kls"]').click();
    await assertCount(mobile, 1);
    assert.equal(await mobile.locator('[data-module="kls"]').getAttribute('aria-pressed'), 'true');

    await mobileBuilder.screenshot({
      path: `${outputDir}/mobile-kls.png`,
      animations: 'disabled'
    });

    assert.deepEqual(mobileErrors, [], `Browser errors detected:\n${mobileErrors.join('\n')}`);
    await mobile.close();

    console.log('Module builder browser preview passed: desktop drag/drop + mobile tap interaction.');
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
