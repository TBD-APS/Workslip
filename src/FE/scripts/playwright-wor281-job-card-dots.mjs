import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const { chromium } = await import('./node_modules/playwright/index.mjs');

const appUrl = process.env.WORKSLIP_LOCAL_APP_URL ?? 'http://127.0.0.1:5270';
const targetRelease = process.env.WORKSLIP_RELEASE_SHA;
const productBlob = process.env.WORKSLIP_PRODUCT_BLOB;
const artifactDir = path.resolve(process.cwd(), '../../artifacts/wor-281-job-card-dots');

if (!targetRelease || !productBlob) throw new Error('Release SHA and product blob are required.');

const statuses = [
  { status: 'InReview', selector: '.review-dot', expected: 'rgb(234, 179, 8)', name: 'review' },
  { status: 'Approved', selector: '.approved-dot', expected: 'rgb(34, 197, 94)', name: 'approved' },
  { status: 'Rejected', selector: '.rejected-dot', expected: 'rgb(239, 68, 68)', name: 'rejected' },
];

const viewports = [
  { name: 'desktop-1280x800', viewport: { width: 1280, height: 800 }, root: '.data-table tbody' },
  { name: 'mobile-390x844', viewport: { width: 390, height: 844 }, root: '.job-card' },
];

await mkdir(artifactDir, { recursive: true });
const browser = await chromium.launch({ headless: true });
const report = {
  releaseSha: targetRelease,
  productBlob,
  browser: 'chromium',
  startedAt: new Date().toISOString(),
  results: [],
};

try {
  for (const view of viewports) {
    const context = await browser.newContext({ viewport: view.viewport, locale: 'da-DK', timezoneId: 'Europe/Copenhagen' });
    const page = await context.newPage();
    const consoleErrors = [];
    const pageErrors = [];
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));

    await page.goto(`${appUrl}/login`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    await page.getByRole('button', { name: 'Dev Login · Admin', exact: true }).click();
    await page.waitForURL((url) => url.pathname.startsWith('/app'), { timeout: 30_000 });

    for (const entry of statuses) {
      await page.goto(`${appUrl}/app?status=${entry.status}`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
      const dot = page.locator(`${view.root} ${entry.selector}`).first();
      await dot.waitFor({ state: 'visible', timeout: 30_000 });
      const actual = await dot.evaluate((element) => getComputedStyle(element).backgroundColor);
      if (actual !== entry.expected) {
        throw new Error(`${view.name}/${entry.status}: expected ${entry.expected}, received ${actual}`);
      }

      const dotCount = await page.locator(`${view.root} ${entry.selector}`).count();
      report.results.push({
        viewport: view.name,
        status: entry.status,
        selector: entry.selector,
        expected: entry.expected,
        actual,
        visibleCount: dotCount,
      });
      await page.screenshot({ path: path.join(artifactDir, `${view.name}-${entry.name}.png`), fullPage: true });
    }

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
    if (overflow) throw new Error(`${view.name}: horizontal document overflow detected.`);
    if (pageErrors.length > 0) throw new Error(`${view.name}: page errors: ${pageErrors.join(' | ')}`);
    if (consoleErrors.length > 0) throw new Error(`${view.name}: console errors: ${consoleErrors.join(' | ')}`);

    report.results.push({ viewport: view.name, pageErrors: 0, consoleErrors: 0, horizontalOverflow: false });
    await context.close();
  }

  report.status = 'passed';
  report.completedAt = new Date().toISOString();
  await writeFile(path.join(artifactDir, 'report.json'), JSON.stringify(report, null, 2));
  console.log('[WOR-281] actual desktop table + mobile job-card dot colors passed.');
} catch (error) {
  report.status = 'failed';
  report.completedAt = new Date().toISOString();
  report.error = error instanceof Error ? error.message : String(error);
  await writeFile(path.join(artifactDir, 'report.json'), JSON.stringify(report, null, 2));
  throw error;
} finally {
  await browser.close();
}
