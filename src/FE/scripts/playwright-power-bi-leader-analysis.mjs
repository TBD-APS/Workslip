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
const USER_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_USER_EMAIL || 'user@17v3ygzs.mailosaur.net').trim();
const UI_TIMEOUT = 25_000;

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });

async function assertNoBrowserErrors(page, label) {
  const pageErrors = [];
  const consoleErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  return () => {
    assert.deepEqual(pageErrors, [], `${label} page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${label} console errors: ${consoleErrors.join(' | ')}`);
  };
}

async function exerciseAdmin(contextOptions, label) {
  const context = await browser.newContext({
    ...contextOptions,
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: ADMIN_EMAIL,
  });

  const page = await context.newPage();
  const verifyErrors = await assertNoBrowserErrors(page, label);

  const analyticsResponse = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return response.request().method() === 'GET'
      && url.pathname === '/api/worksheets/all/report/power-bi/data';
  }, { timeout: UI_TIMEOUT });

  const navigation = await page.goto(`${APP_URL}/app/lederanalyse`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `${label} Lederanalyse returned HTTP ${navigation?.status() ?? 'unknown'}.`);
  assert.equal((await analyticsResponse).status(), 200, `${label} analytics endpoint must return 200 for Admin.`);

  await page.locator('#leader-analysis-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.locator('#leader-analysis-powerbi').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.locator('#admin-power-bi-job-status').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.locator('#overview-power-bi-heading').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.locator('#leader-analysis-kpi-active').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  await page.locator('#overview-analytics-tab-employees').click();
  const employeePanel = page.locator('#overview-analytics-panel-employees');
  await employeePanel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  assert.match(await employeePanel.textContent() ?? '', /fakturerbar værdi/i, `${label} employee analytics must explain billable value.`);

  await page.locator('#overview-analytics-tab-customers').click();
  await page.locator('#overview-analytics-panel-customers').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  if (label === 'admin-mobile') {
    const viewport = page.viewportSize();
    assert.ok(viewport && viewport.width <= 430, `${label} must run at a phone-sized viewport.`);
    const bodyWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    assert.ok(bodyWidth <= viewport.width + 2, `${label} must not introduce horizontal page overflow.`);
  }

  const reportConfigResponsePromise = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return response.request().method() === 'GET'
      && url.pathname === '/api/worksheets/all/report/power-bi';
  }, { timeout: UI_TIMEOUT });

  const timerNavigation = await page.goto(`${APP_URL}/app/timer`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(timerNavigation?.ok(), `${label} Timer returned HTTP ${timerNavigation?.status() ?? 'unknown'}.`);

  const reportConfigResponse = await reportConfigResponsePromise;
  assert.equal(reportConfigResponse.status(), 200, `${label} Power BI report config must return 200 for Admin.`);
  const reportConfig = await reportConfigResponse.json();

  if (reportConfig?.url) {
    await page.locator('#timer-power-bi-report').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.locator('#power-bi-report-title').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const embeddedFrame = page.locator('#timer-power-bi-frame');
    if (reportConfig.embedUrl) {
      await embeddedFrame.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      const src = await embeddedFrame.getAttribute('src');
      assert.match(src ?? '', /^https:\/\/app\.powerbi\.com\/reportEmbed\?/i, `${label} Timer must use the secure Power BI embed endpoint.`);
    } else {
      assert.equal(await embeddedFrame.count(), 0, `${label} Timer must not render an iframe without a secure embed URL.`);
      assert.match(
        await page.locator('#timer-power-bi-report').textContent() ?? '',
        /Rapporten kan ikke indlejres sikkert/i,
        `${label} Timer must explain why a configured report cannot be embedded.`,
      );
    }
  } else {
    // Wait on the Timer page's stable container rather than networkidle (which
    // flakes when background polling keeps the connection busy) before asserting
    // that Power BI is absent.
    await page.locator('#timer-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await page.locator('#timer-power-bi-report').count(), 0, `${label} must hide unconfigured Power BI from the product UI.`);
    assert.equal(await page.locator('#timer-power-bi-frame').count(), 0, `${label} must not render an unconfigured Power BI iframe.`);
  }

  if (label === 'admin-mobile') {
    const viewport = page.viewportSize();
    const bodyWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    assert.ok(viewport && bodyWidth <= viewport.width + 2, `${label} Timer Power BI surface must not introduce horizontal overflow.`);
  }

  verifyErrors();
  await context.close();
}

async function exerciseNormalUser() {
  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: USER_EMAIL,
  });

  const page = await context.newPage();
  const verifyErrors = await assertNoBrowserErrors(page, 'normal-user');
  let analyticsRequests = 0;
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname === '/api/worksheets/all/report/power-bi/data') {
      analyticsRequests += 1;
    }
  });

  const navigation = await page.goto(`${APP_URL}/app/overblik`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Normal-user Overview returned HTTP ${navigation?.status() ?? 'unknown'}.`);
  await page.locator('#recent-jobs-heading').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  // The Overview heading above is the authoritative "page rendered" signal;
  // asserting absence after it is reliable without a networkidle wait.
  assert.equal(await page.locator('#admin-power-bi-job-status').count(), 0, 'Normal user must not render the Admin analytics dashboard.');
  assert.equal(await page.locator('#favorite-customers-heading').count(), 0, 'Normal user must not render Admin favorite customers.');
  assert.equal(await page.locator('#recent-documents-heading').count(), 0, 'Normal user must not render Admin latest documents.');
  assert.equal(analyticsRequests, 0, 'Normal user must not request the Admin analytics endpoint.');

  verifyErrors();
  await context.close();
}

try {
  await exerciseAdmin({ viewport: { width: 1280, height: 800 } }, 'admin-desktop');
  await exerciseAdmin(devices['iPhone 13'], 'admin-mobile');
  await exerciseNormalUser();
  console.log('[playwright] Live Admin Lederanalyse + Timer Power BI integration passed on desktop/mobile.');
} finally {
  await browser.close();
}
