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
const SUPERADMIN_EMAIL = String(
  process.env.WORKSLIP_PLAYWRIGHT_SUPERADMIN_EMAIL || 'superadmin@17v3ygzs.mailosaur.net',
).trim();
const UI_TIMEOUT = 25_000;
const VIEWPORTS = [
  { name: 'desktop-1280', width: 1280, height: 800 },
  { name: 'mobile-390', width: 390, height: 844 },
];

const { chromium } = await import('playwright');
const browser = await chromium.launch({ headless: true });

try {
  for (const viewport of VIEWPORTS) {
    console.log(`[playwright] SuperAdmin statistics: ${viewport.name}.`);
    await verifyStatisticsViewport(viewport);
  }
  console.log('[playwright] SuperAdmin statistics responsive evidence passed.');
} finally {
  await browser.close();
}

async function verifyStatisticsViewport(viewport) {
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    viewport: { width: viewport.width, height: viewport.height },
  });

  await context.route(
    (url) => !['127.0.0.1', 'localhost'].includes(url.hostname),
    (route) => route.fulfill({ status: 204, contentType: 'application/javascript', body: '' }),
  );

  const bootstrap = await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: SUPERADMIN_EMAIL,
  });
  assert.equal(String(bootstrap.user.role).toLowerCase(), 'superadmin', 'Synthetic Superadmin identity resolved unexpectedly.');

  const page = await context.newPage();
  const pageErrors = [];
  const consoleErrors = [];
  const failedApiRequests = [];
  const failedApiResponses = [];

  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('requestfailed', (request) => {
    const failure = request.failure()?.errorText ?? 'unknown';
    if (request.url().includes('/api/') && !/ERR_ABORTED/i.test(failure)) {
      failedApiRequests.push(`${request.method()} ${new URL(request.url()).pathname} ${failure}`);
    }
  });
  page.on('response', (response) => {
    if (response.url().includes('/api/') && response.status() >= 400) {
      failedApiResponses.push(`${response.request().method()} ${new URL(response.url()).pathname} ${response.status()}`);
    }
  });

  try {
    const statisticsResponse = page.waitForResponse(
      (response) => response.request().method() === 'GET'
        && new URL(response.url()).pathname === '/api/superadmin/analytics/workflow-statistics',
      { timeout: UI_TIMEOUT },
    );

    await page.goto(`${APP_URL}/superadmin/statistik`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });

    const response = await statisticsResponse;
    assert.equal(response.status(), 200, `Statistics API returned HTTP ${response.status()} at ${viewport.name}.`);
    await page.waitForURL((url) => url.pathname === '/superadmin/statistik', { timeout: UI_TIMEOUT });
    await page.waitForFunction(
      () => document.querySelector('.superadmin-statistics-page') !== null
        && document.querySelector('.statistics-loading-state') === null,
      undefined,
      { timeout: UI_TIMEOUT },
    );

    const layout = await page.evaluate(() => {
      const root = document.querySelector('.superadmin-statistics-page');
      const filters = document.querySelector('.statistics-filters');
      const cards = [...document.querySelectorAll('.statistics-metric-card')];
      const documentWidth = document.documentElement.clientWidth;
      const scrollWidth = document.documentElement.scrollWidth;
      const rootRect = root?.getBoundingClientRect() ?? null;
      const filterRect = filters?.getBoundingClientRect() ?? null;

      return {
        documentWidth,
        scrollWidth,
        hasRoot: Boolean(root),
        hasErrorState: Boolean(document.querySelector('.statistics-error-state')),
        metricCardCount: cards.length,
        rootRight: rootRect?.right ?? null,
        filterRight: filterRect?.right ?? null,
      };
    });

    assert.equal(layout.hasRoot, true, `Statistics page did not render at ${viewport.name}.`);
    assert.equal(layout.hasErrorState, false, `Statistics page rendered an error state at ${viewport.name}.`);
    assert.ok(layout.metricCardCount >= 3, `Statistics KPI cards were incomplete at ${viewport.name}.`);
    assert.ok(
      layout.scrollWidth <= layout.documentWidth + 1,
      `Statistics page overflowed horizontally at ${viewport.name}: ${layout.scrollWidth}px > ${layout.documentWidth}px.`,
    );
    assert.ok(
      layout.rootRight === null || layout.rootRight <= viewport.width + 1,
      `Statistics root exceeded the ${viewport.name} viewport.`,
    );
    assert.ok(
      layout.filterRight === null || layout.filterRight <= viewport.width + 1,
      `Statistics filters exceeded the ${viewport.name} viewport.`,
    );

    assert.deepEqual(pageErrors, [], `${viewport.name} page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${viewport.name} console errors: ${consoleErrors.join(' | ')}`);
    assert.deepEqual(failedApiRequests, [], `${viewport.name} failed API requests: ${failedApiRequests.join(' | ')}`);
    assert.deepEqual(failedApiResponses, [], `${viewport.name} failed API responses: ${failedApiResponses.join(' | ')}`);
  } finally {
    await context.close();
  }
}
