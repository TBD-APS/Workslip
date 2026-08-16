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
const LARGE_UPLOAD_TIMEOUT = 60_000;
const ATTACHMENT_SIZE_BYTES = 75 * 1024 * 1024;

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });

try {
  await verifyDocumentAttachmentBoundary();
  console.log('[playwright] 75 MB document attachment regression passed.');
} finally {
  await browser.close();
}

async function verifyDocumentAttachmentBoundary() {
  const context = await browser.newContext({
    ...devices['iPhone 13'],
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  const auth = await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email: ADMIN_EMAIL,
  });
  assert.equal(String(auth.user.role).toLowerCase(), 'admin', 'Synthetic browser identity must resolve to Admin.');

  let documentId = null;
  try {
    const createResponse = await fetch(`${API_URL}/api/docs/`, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${auth.token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        title: `Upload cap ${Date.now()}`,
        content: 'Disposable Playwright document for attachment-boundary validation.',
        tags: ['playwright'],
      }),
      signal: AbortSignal.timeout(UI_TIMEOUT),
    });
    assert.ok(createResponse.ok, `Document fixture create returned HTTP ${createResponse.status}.`);
    const document = await createResponse.json();
    documentId = document?.id ?? null;
    assert.ok(documentId, 'Document fixture response did not contain an id.');

    const page = await context.newPage();
    const pageErrors = [];
    page.on('pageerror', (error) => pageErrors.push(error.message));

    const meResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'GET'
        && new URL(response.url()).pathname === '/api/auth/me',
    { timeout: UI_TIMEOUT });
    const navigation = await page.goto(`${APP_URL}/app/docs/${documentId}`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    assert.ok(navigation?.ok(), `Document route returned HTTP ${navigation?.status() ?? 'unknown'}.`);
    const meResponse = await meResponsePromise;
    assert.equal(meResponse.status(), 200, `/api/auth/me returned HTTP ${meResponse.status()}.`);
    await page.locator('.app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.locator('input.docs-file-input[type="file"]').waitFor({ state: 'attached', timeout: UI_TIMEOUT });

    const largeMp3 = Buffer.alloc(ATTACHMENT_SIZE_BYTES);
    largeMp3.set([0x49, 0x44, 0x33, 0x04]);

    const uploadResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'POST'
        && new URL(response.url()).pathname === `/api/docs/${documentId}/attachments`,
    { timeout: LARGE_UPLOAD_TIMEOUT });
    await page.locator('input.docs-file-input[type="file"]').setInputFiles({
      name: 'boundary-75mb.mp3',
      mimeType: 'audio/mpeg',
      buffer: largeMp3,
    });
    const uploadResponse = await uploadResponsePromise;
    assert.ok(uploadResponse.ok(), `75 MB document attachment returned HTTP ${uploadResponse.status()}.`);

    await page.getByText('Filen er tilføjet.', { exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.getByText('boundary-75mb.mp3', { exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.match(await page.locator('.docs-attachments-help').innerText(), /maks\. 75 MB pr\. fil/);
    assert.deepEqual(pageErrors, [], `Browser page errors: ${pageErrors.join(' | ')}`);
  } finally {
    if (documentId) {
      await fetch(`${API_URL}/api/docs/${documentId}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${auth.token}` },
        signal: AbortSignal.timeout(UI_TIMEOUT),
      }).catch(() => undefined);
    }
    await context.close();
  }
}
