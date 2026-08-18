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

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });

try {
  await verifyTimerPreview({ viewport: { width: 1280, height: 800 } }, 'desktop-1280');
  await verifyTimerPreview(devices['iPhone 13'], 'mobile-390');
  await verifyJobPreviewDownloadReuse();
  console.log('[playwright] PDF performance browser evidence passed: worksheet + job-wizard; desktop-1280 + mobile-390; page errors 0; console errors 0.');
} finally {
  await browser.close();
}

async function authenticatedContext(contextOptions) {
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    acceptDownloads: true,
    ...contextOptions,
  });
  const session = await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: ADMIN_EMAIL,
  });
  assert.equal(String(session.user.role).toLowerCase(), 'admin', 'Synthetic PDF browser identity must resolve to Admin.');
  return { context, session };
}

async function openAuthenticatedApp(context, path) {
  const page = await context.newPage();
  const pageErrors = [];
  const consoleErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });

  const meResponse = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/auth/me',
  { timeout: UI_TIMEOUT });

  const navigation = await page.goto(`${APP_URL}${path}`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  assert.ok(navigation?.ok(), `Authenticated PDF navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);
  assert.equal((await meResponse).status(), 200, 'Authenticated PDF flow requires successful /api/auth/me.');
  await page.locator('.app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  return {
    page,
    assertCleanBrowser() {
      assert.deepEqual(pageErrors, [], `PDF flow page errors: ${pageErrors.join(' | ')}`);
      assert.deepEqual(consoleErrors, [], `PDF flow console errors: ${consoleErrors.join(' | ')}`);
    },
  };
}

async function verifyTimerPreview(contextOptions, viewportLabel) {
  const { context } = await authenticatedContext(contextOptions);
  try {
    const flow = await openAuthenticatedApp(context, '/app/timer');
    const { page } = flow;
    const previewButton = page.getByRole('button', { name: 'Vis PDF' });
    await previewButton.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await previewButton.isDisabled(), false, `${viewportLabel}: seeded current month must allow PDF preview.`);

    const previewResponsePromise = page.waitForResponse((response) => {
      const url = new URL(response.url());
      return response.request().method() === 'GET'
        && url.pathname === '/api/worksheets/all/report/pdf/preview';
    }, { timeout: UI_TIMEOUT });

    await previewButton.click();
    const previewResponse = await previewResponsePromise;
    assert.equal(previewResponse.status(), 200, `${viewportLabel}: Timer PDF preview endpoint must return 200.`);

    const dialog = page.getByRole('dialog', { name: /PDF-preview af timer/i });
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const firstPage = dialog.locator('img.hours-pdf-preview-page').first();
    await firstPage.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await firstPage.evaluate((image) => image.decode?.()).catch(() => {});

    const dimensions = await firstPage.evaluate(async (image) => {
      const response = await fetch(image.src);
      const bytes = await response.arrayBuffer();
      const view = new DataView(bytes);
      return {
        width: view.getUint32(16, false),
        height: view.getUint32(20, false),
      };
    });
    assert.ok(dimensions.width >= 1000 && dimensions.width <= 1200, `${viewportLabel}: unexpected raw preview width ${dimensions.width}.`);
    assert.ok(dimensions.height >= 700 && dimensions.height <= 850, `${viewportLabel}: unexpected raw preview height ${dimensions.height}.`);

    await dialog.getByRole('button', { name: 'Luk PDF-preview' }).click();
    await dialog.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    flow.assertCleanBrowser();
    console.log(`[playwright] worksheet PDF preview passed on ${viewportLabel} at raw ${dimensions.width}x${dimensions.height}.`);
  } finally {
    await context.close();
  }
}

async function verifyJobPreviewDownloadReuse() {
  const { context, session } = await authenticatedContext({ viewport: { width: 1280, height: 800 } });
  try {
    const jobsResponse = await fetch(`${API_URL}/api/jobs?limit=50&offset=0`, {
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${session.token}`,
      },
    });
    assert.equal(jobsResponse.status, 200, `Seeded job lookup returned HTTP ${jobsResponse.status}.`);
    const jobsPayload = await jobsResponse.json();
    const jobs = Array.isArray(jobsPayload?.items) ? jobsPayload.items : [];
    const job = jobs.find((candidate) => String(candidate.status).toLowerCase() !== 'draft') ?? jobs[0];
    assert.ok(job?.id, 'Seeded PDF browser fixture must contain at least one job.');

    const flow = await openAuthenticatedApp(context, `/app/completed/${job.id}`);
    const { page } = flow;
    const pdfPath = `/api/jobs/${job.id}/report/pdf`;
    let pdfGetCount = 0;
    page.on('request', (request) => {
      if (request.method() === 'GET' && new URL(request.url()).pathname === pdfPath) {
        pdfGetCount += 1;
      }
    });

    const previewButton = page.getByRole('button', { name: 'Forhåndsvis PDF' });
    await previewButton.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const previewResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'GET'
        && new URL(response.url()).pathname === pdfPath,
    { timeout: UI_TIMEOUT });

    await previewButton.click();
    const previewResponse = await previewResponsePromise;
    assert.equal(previewResponse.status(), 200, 'Job PDF preview endpoint must return 200.');
    await page.waitForFunction(() => {
      const button = document.querySelector('button[aria-label="Forhåndsvis PDF"]');
      return button instanceof HTMLButtonElement && !button.disabled;
    }, undefined, { timeout: UI_TIMEOUT });
    assert.equal(pdfGetCount, 1, 'Job PDF preview must issue exactly one PDF GET.');

    const downloadButton = page.getByRole('button', { name: 'Download PDF' });
    const downloadPromise = page.waitForEvent('download', { timeout: UI_TIMEOUT });
    await downloadButton.click();
    const download = await downloadPromise;
    assert.ok(download.suggestedFilename().toLowerCase().endsWith('.pdf'), 'Job PDF download must retain a PDF filename.');
    assert.equal(pdfGetCount, 1, 'Immediate download after preview must reuse the previewed Blob instead of issuing a second PDF GET.');

    flow.assertCleanBrowser();
    console.log('[playwright] job-wizard PDF preview -> download reuse passed with one PDF GET.');
  } finally {
    await context.close();
  }
}
