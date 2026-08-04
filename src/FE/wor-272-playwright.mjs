import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const baseUrl = process.env.WOR_272_BASE_URL ?? 'http://127.0.0.1:4173';
const browser = await chromium.launch();
const context = await browser.newContext({
  viewport: { width: 390, height: 844 },
  isMobile: true,
  hasTouch: true,
  deviceScaleFactor: 3,
});
const page = await context.newPage();
const client = await context.newCDPSession(page);
const consoleErrors = [];
const pageErrors = [];
const failedRequests = [];

page.on('console', (message) => {
  if (message.type() === 'error') consoleErrors.push(message.text());
});
page.on('pageerror', (error) => pageErrors.push(error.message));
page.on('requestfailed', (request) => {
  failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`.trim());
});

async function dispatchTouch(type, points) {
  await client.send('Input.dispatchTouchEvent', {
    type,
    touchPoints: points.map(({ x, y, id = 1 }) => ({ x, y, id })),
  });
}

async function reopenDrawer() {
  const drawer = page.locator('.drawer');
  if (!(await drawer.evaluate((element) => element.classList.contains('open')))) {
    await page.getByRole('button', { name: 'Åbn drawer' }).click();
    await page.waitForFunction(() => document.querySelector('.drawer')?.classList.contains('open'));
  }
}

try {
  await page.goto(`${baseUrl}/wor-272-validation.html`, { waitUntil: 'networkidle' });

  const drawer = page.getByRole('dialog', { name: 'Valideringsdrawer' });
  await drawer.waitFor({ state: 'visible' });
  assert.equal(await page.locator('.drawer').count(), 1, 'Exactly one shared drawer should render.');
  assert.equal(await page.getByTestId('current-route').textContent(), '/app');
  assert.equal(await page.getByTestId('previous-route').count(), 0);

  await dispatchTouch('touchStart', [{ x: 8, y: 180 }]);
  await dispatchTouch('touchMove', [{ x: 42, y: 180 }]);
  assert.equal(await drawer.evaluate((element) => element.classList.contains('drawer-dragging')), true);
  assert.equal(await drawer.evaluate((element) => element.style.getPropertyValue('--drawer-drag-x')), '34px');
  assert.equal(await page.getByTestId('current-route').textContent(), '/app');
  assert.equal(await page.getByTestId('previous-route').count(), 0);
  await dispatchTouch('touchEnd', []);
  await page.waitForTimeout(400);
  assert.equal(await drawer.evaluate((element) => element.classList.contains('open')), true, 'Short swipe should return the drawer to open.');

  await dispatchTouch('touchStart', [{ x: 60, y: 240, id: 2 }]);
  await dispatchTouch('touchMove', [{ x: 220, y: 240, id: 2 }]);
  assert.equal(await drawer.evaluate((element) => element.classList.contains('drawer-dragging')), false, 'Touches away from the edge must remain untouched.');
  await dispatchTouch('touchEnd', []);
  assert.equal(await drawer.evaluate((element) => element.classList.contains('open')), true);

  await dispatchTouch('touchStart', [{ x: 8, y: 300, id: 3 }]);
  await dispatchTouch('touchMove', [{ x: 140, y: 300, id: 3 }]);
  assert.equal(await drawer.evaluate((element) => element.style.getPropertyValue('--drawer-drag-x')), '132px');
  assert.equal(await page.locator('.drawer').count(), 1);
  assert.equal(await page.getByTestId('current-route').textContent(), '/app');
  assert.equal(await page.getByTestId('previous-route').count(), 0);
  await dispatchTouch('touchEnd', []);
  await page.waitForFunction(() => !document.querySelector('.drawer')?.classList.contains('open'));
  assert.equal(await page.getByTestId('current-route').textContent(), '/app', 'Completed drawer swipe must not navigate.');
  assert.equal(new URL(page.url()).pathname, '/wor-272-validation.html');

  await reopenDrawer();
  await page.getByRole('button', { name: 'Tilbage fra valideringsdrawer' }).click();
  await page.waitForFunction(() => !document.querySelector('.drawer')?.classList.contains('open'));

  assert.deepEqual(consoleErrors, [], `Console errors: ${consoleErrors.join('\n')}`);
  assert.deepEqual(pageErrors, [], `Page errors: ${pageErrors.join('\n')}`);
  assert.deepEqual(failedRequests, [], `Failed requests: ${failedRequests.join('\n')}`);

  console.log('WOR-272 drawer gesture validation passed', {
    browser: 'Chromium',
    viewport: '390x844',
    shortSwipeReturnedOpen: true,
    nonEdgeTouchIgnored: true,
    completedSwipeClosedWithoutNavigation: true,
    consoleErrors: consoleErrors.length,
    pageErrors: pageErrors.length,
    failedRequests: failedRequests.length,
  });
} finally {
  await context.close();
  await browser.close();
}
