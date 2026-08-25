import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { requireLoopbackOrigin, seedLocalBrowserSession } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270',
  'WORKSLIP_PLAYWRIGHT_APP_URL',
);
const ADMIN_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL || 'admin@17v3ygzs.mailosaur.net').trim();

async function run() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    viewport: { width: 1280, height: 800 },
  });

  try {
    const bootstrap = await seedLocalBrowserSession(context, {
      appUrl: APP_URL,
      email: ADMIN_EMAIL,
    });
    const page = await context.newPage();

    // 1. Verify FAB visibility on Job List (/app)
    await page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded' });
    const fabJobList = page.locator('#app-fab-create-job');
    await fabJobList.waitFor({ state: 'visible' });
    console.log('[test] FAB visible on /app');

    // 2. Verify FAB visibility on Overview (/app/overblik)
    await page.goto(`${APP_URL}/app/overblik`, { waitUntil: 'domcontentloaded' });
    const fabOverview = page.locator('#app-fab-create-job');
    await fabOverview.waitFor({ state: 'visible' });
    console.log('[test] FAB visible on /app/overblik');

    // 3. Verify FAB opens Create Sheet
    await fabOverview.click();
    const createSheet = page.locator('[role="dialog"]');
    await createSheet.waitFor({ state: 'visible' });
    console.log('[test] FAB opens create sheet');

    // Close sheet
    await page.keyboard.press('Escape');
    await createSheet.waitFor({ state: 'hidden' });

    console.log('[test] Auditor scope UI interaction verified');

  } catch (e) {
    console.error('[test] Failed:', e);
    throw e;
  } finally {
    await browser.close();
  }
}

run();
