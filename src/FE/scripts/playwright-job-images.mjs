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
const API_TIMEOUT = 30_000;
const PNG_FIXTURE = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2L9sAAAAASUVORK5CYII=',
  'base64',
);

const { chromium } = await import('playwright');
const browser = await chromium.launch({ headless: true });

try {
  await verifyGallery({ name: 'desktop-1280', viewport: { width: 1280, height: 800 } });
  await verifyGallery({ name: 'mobile-390', viewport: { width: 390, height: 844 } });
  console.log('[playwright] job image collapsed/lazy gallery evidence passed: desktop-1280 + mobile-390; page errors 0; console errors 0.');
} finally {
  await browser.close();
}

async function verifyGallery({ name, viewport }) {
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    viewport,
  });

  try {
    const bootstrap = await seedLocalBrowserSession(context, {
      appUrl: APP_URL,
      apiUrl: API_URL,
      email: ADMIN_EMAIL,
    });
    assert.equal(String(bootstrap.user.role).toLowerCase(), 'admin', `${name}: synthetic identity must be Admin.`);

    let idempotencySequence = 0;
    const apiJson = async (method, pathname, body, expectedStatuses = [200]) => {
      const normalizedMethod = method.toUpperCase();
      const mutationHeaders = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(normalizedMethod)
        ? { 'Idempotency-Key': `playwright-job-images-${name}-${++idempotencySequence}` }
        : {};
      const response = await fetch(`${API_URL}${pathname}`, {
        method: normalizedMethod,
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${bootstrap.token}`,
          ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
          ...mutationHeaders,
        },
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: AbortSignal.timeout(API_TIMEOUT),
      });
      const contentType = response.headers.get('content-type') ?? '';
      const payload = contentType.includes('json')
        ? await response.json().catch(() => null)
        : await response.text().catch(() => null);
      assert.ok(
        expectedStatuses.includes(response.status),
        `${name}: ${normalizedMethod} ${pathname} returned HTTP ${response.status}; expected ${expectedStatuses.join('/')}.`,
      );
      return payload;
    };

    const unique = `${Date.now()}-${name}`;
    const created = await apiJson('POST', '/api/jobs', {
      customerId: null,
      customerSnapshot: {
        name: `Playwright Images ${unique}`,
        email: `images-${unique}@example.test`,
        phone: '20112233',
        address: 'Testvej 1, 8000 Aarhus C',
        contactPerson: `Billedtest ${unique}`,
      },
      createCustomerFromSnapshot: false,
      destinationAddress: 'Testvej 1',
      destinationZipCode: '8000',
      destinationCity: 'Aarhus C',
      jobType: 'KLS',
      assignedUserIds: [bootstrap.user.userId],
      duplicatePerAssignedUser: false,
      linkedJobIds: [],
      work: null,
      observations: {
        reportDate: null,
        taskDescription: `Billedgalleri ${unique}`,
        customerObservations: null,
        technicalObservations: null,
      },
    }, [200, 201]);
    assert.ok(created?.id, `${name}: image gallery fixture creation returned no job id.`);
    const jobId = created.id;
    const sectionId = `job-images-section-${jobId}`;
    const gridId = `job-images-grid-${jobId}`;
    const toggleId = `job-images-toggle-${jobId}`;
    const libraryInputId = `job-images-library-input-${jobId}`;

    const page = await context.newPage();
    const pageErrors = [];
    const consoleErrors = [];
    const imageGetPaths = [];
    page.on('pageerror', (error) => pageErrors.push(error.message));
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });
    page.on('request', (request) => {
      if (request.method() !== 'GET') return;
      const pathname = new URL(request.url()).pathname;
      if (pathname.startsWith(`/api/jobs/${jobId}/images/`)) imageGetPaths.push(pathname);
    });

    const navigation = await page.goto(`${APP_URL}/app/job/${jobId}`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    assert.ok(navigation?.ok(), `${name}: job navigation returned HTTP ${navigation?.status() ?? 'unknown'}.`);

    const section = page.locator(`#${sectionId}`);
    await section.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await section.scrollIntoViewIfNeeded();

    const libraryInput = page.locator(`#${libraryInputId}`);
    await libraryInput.waitFor({ state: 'attached', timeout: UI_TIMEOUT });
    const files = Array.from({ length: 6 }, (_, index) => ({
      name: `gallery-${index + 1}.png`,
      mimeType: 'image/png',
      buffer: PNG_FIXTURE,
    }));
    await libraryInput.setInputFiles(files, { timeout: UI_TIMEOUT });

    const toggleButton = page.locator(`#${toggleId}`);
    await toggleButton.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const listedImages = await apiJson('GET', `/api/jobs/${jobId}/images`);
    assert.ok(Array.isArray(listedImages), `${name}: job image list must be an array.`);
    assert.equal(listedImages.length, 6, `${name}: fixture must contain exactly six images.`);

    // On a phone, the newly rendered grid can start just below the lazy-image
    // boundary even though the section itself was previously in view. Put each
    // collapsed tile in view before measuring image requests, as a user scrolling
    // through the gallery would.
    for (const image of listedImages.slice(0, 4)) {
      const tile = page.locator(`#job-image-tile-${image.id}`);
      await tile.waitFor({ state: 'attached', timeout: UI_TIMEOUT });
      await tile.scrollIntoViewIfNeeded();
    }

    await waitUntil(
      () => uniqueImageGetCount(imageGetPaths) >= 4,
      `${name}: first four image requests did not complete their lazy-start boundary.`,
    );

    for (const image of listedImages.slice(0, 4)) {
      assert.equal(await page.locator(`#job-image-tile-${image.id}`).count(), 1, `${name}: collapsed gallery must mount image ${image.id}.`);
    }
    for (const image of listedImages.slice(4)) {
      assert.equal(await page.locator(`#job-image-tile-${image.id}`).count(), 0, `${name}: collapsed gallery must not mount image ${image.id}.`);
    }
    assert.equal(uniqueImageGetCount(imageGetPaths), 4, `${name}: collapsed gallery must request exactly four image blobs before expansion.`);
    assert.equal(await toggleButton.getAttribute('aria-expanded'), 'false', `${name}: collapsed gallery must expose aria-expanded=false.`);

    await toggleButton.click();
    await waitUntil(
      async () => (await toggleButton.getAttribute('aria-expanded')) === 'true',
      `${name}: gallery toggle did not enter expanded state.`,
    );
    for (const image of listedImages) {
      const tile = page.locator(`#job-image-tile-${image.id}`);
      await tile.waitFor({ state: 'attached', timeout: UI_TIMEOUT });
      await tile.scrollIntoViewIfNeeded();
    }
    await waitUntil(
      () => uniqueImageGetCount(imageGetPaths) >= 6,
      `${name}: expanded gallery did not start the remaining image requests.`,
    );

    assert.equal(uniqueImageGetCount(imageGetPaths), 6, `${name}: expansion must request each of the six images once.`);
    assert.equal(await toggleButton.getAttribute('aria-expanded'), 'true', `${name}: expanded gallery must expose aria-expanded=true.`);
    await toggleButton.click();
    await waitUntil(
      async () => (await toggleButton.getAttribute('aria-expanded')) === 'false',
      `${name}: gallery did not return to collapsed state.`,
    );
    for (const image of listedImages.slice(4)) {
      assert.equal(await page.locator(`#job-image-tile-${image.id}`).count(), 0, `${name}: collapsed gallery must unmount image ${image.id}.`);
    }
    await page.locator(`#${gridId}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    assert.deepEqual(pageErrors, [], `${name}: browser page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${name}: browser console errors: ${consoleErrors.join(' | ')}`);
  } finally {
    await context.close();
  }
}

function uniqueImageGetCount(paths) {
  return new Set(paths).size;
}

async function waitUntil(predicate, errorMessage) {
  const deadline = Date.now() + UI_TIMEOUT;
  while (Date.now() < deadline) {
    if (await predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  throw new Error(errorMessage);
}
