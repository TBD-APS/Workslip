import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { chromium, devices } from 'playwright';

const APP_URL = (process.env.PROD_URL ?? '').replace(/\/+$/, '');
const VIEWPORT_NAME = 'iPhone 13';
const ARTIFACT_DIR = path.resolve(process.cwd(), '../../artifacts/playwright-prod-smoke');
const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;
const TARGET_PATH = '/app/profil';
const RUNTIME_ASSET_CACHE = 'workslip-route-assets-v1';
const NOTIFICATION_TAG = `playwright-wor-248-${Date.now()}`;

if (!APP_URL) throw new Error('PROD_URL is required.');

await mkdir(ARTIFACT_DIR, { recursive: true });

const report = {
  scenario: 'notification-navigation',
  appUrl: APP_URL,
  targetPath: TARGET_PATH,
  startedAt: new Date().toISOString(),
  browser: 'chromium',
  viewport: devices[VIEWPORT_NAME].viewport,
  steps: [],
  consoleErrors: [],
  pageErrors: [],
  failedRequests: [],
  failedApiResponses: [],
  cacheAudit: null,
  coverageNotes: [
    {
      area: 'Push delivery boundary',
      detail: 'Dispatches a standards-based PushEvent inside the deployed service worker. It validates Workslip push handling, notification creation, notificationclick routing, and cache isolation, but not the operating-system notification tray or the external push provider transport.',
    },
    {
      area: 'Fallback branches',
      detail: 'This browser scenario validates the open-client router acknowledgement path. Document-navigation and new-window fallbacks remain covered by the notification navigation unit tests.',
    },
  ],
};

let browser;
let context;
let page;
let serviceWorker;
let suiteFailure = null;

try {
  browser = await chromium.launch();
  context = await browser.newContext({
    ...devices[VIEWPORT_NAME],
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  await context.grantPermissions(['notifications'], { origin: APP_URL });

  page = await context.newPage();

  await step('admin login', async () => {
    await page.goto(`${APP_URL}/login`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    const button = page.getByRole('button', { name: 'Dev Login · Admin', exact: true });
    await button.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const tokenResponsePromise = page.waitForResponse(
      (response) => response.request().method() === 'POST'
        && pathname(response.url()) === '/api/dev/token',
      { timeout: API_TIMEOUT },
    );

    await button.click();
    const tokenResponse = await tokenResponsePromise;
    if (!tokenResponse.ok()) {
      throw new Error(`Dev login returned HTTP ${tokenResponse.status()}.`);
    }

    await page.waitForURL((url) => url.pathname.startsWith('/app'), { timeout: API_TIMEOUT });
  });

  // Diagnostics start after authentication so expected unauthenticated startup probes
  // cannot create false failures for the service-worker regression scenario.
  attachPageDiagnostics(page);

  await step('deployed service worker controls the app', async () => {
    await page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded', timeout: 45_000 });

    const readyState = await page.evaluate(async () => {
      if (!('serviceWorker' in navigator)) {
        return { supported: false, controlled: false, registrationActive: false };
      }

      const registration = await navigator.serviceWorker.ready;
      if (!navigator.serviceWorker.controller) {
        await new Promise((resolve) => {
          const timeout = window.setTimeout(resolve, 5_000);
          navigator.serviceWorker.addEventListener('controllerchange', () => {
            window.clearTimeout(timeout);
            resolve(undefined);
          }, { once: true });
        });
      }

      return {
        supported: true,
        controlled: Boolean(navigator.serviceWorker.controller),
        registrationActive: Boolean(registration.active),
        scriptUrl: registration.active?.scriptURL ?? null,
      };
    });

    if (!readyState.supported || !readyState.registrationActive) {
      throw new Error('The deployed application has no active service worker.');
    }

    if (!readyState.controlled) {
      await page.reload({ waitUntil: 'domcontentloaded', timeout: 45_000 });
      const controlled = await page.evaluate(() => Boolean(navigator.serviceWorker.controller));
      if (!controlled) throw new Error('The deployed service worker did not take control after reload.');
    }

    serviceWorker = context.serviceWorkers().find((worker) =>
      new URL(worker.url()).origin === APP_URL,
    );
    serviceWorker ??= await context.waitForEvent('serviceworker', {
      predicate: (worker) => new URL(worker.url()).origin === APP_URL,
      timeout: UI_TIMEOUT,
    });

    report.serviceWorkerUrl = serviceWorker.url();
  });

  await step('notification click routes the existing app client', async () => {
    await page.bringToFront();
    await page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    await page.waitForURL((url) => url.pathname === '/app', { timeout: UI_TIMEOUT });

    const pagesBefore = context.pages();
    const pageCountBefore = pagesBefore.length;
    if (!pagesBefore.includes(page)) throw new Error('The authenticated application page is not open.');

    const clickResult = await serviceWorker.evaluate(async ({ tag, targetPath }) => {
      const oldNotifications = await self.registration.getNotifications({ tag });
      for (const notification of oldNotifications) notification.close();

      if (typeof PushEvent !== 'function') {
        throw new Error('PushEvent is unavailable in the deployed service-worker runtime.');
      }
      if (typeof NotificationEvent !== 'function') {
        throw new Error('NotificationEvent is unavailable in the deployed service-worker runtime.');
      }

      self.dispatchEvent(new PushEvent('push', {
        data: JSON.stringify({
          title: 'Workslip Playwright',
          options: {
            body: 'WOR-248 notification navigation validation',
            tag,
            data: { url: targetPath },
          },
        }),
      }));

      let notification = null;
      const deadline = Date.now() + 5_000;
      while (!notification && Date.now() < deadline) {
        [notification] = await self.registration.getNotifications({ tag });
        if (!notification) await new Promise((resolve) => setTimeout(resolve, 100));
      }

      if (!notification) {
        throw new Error('The deployed service worker did not create the test notification.');
      }

      const result = {
        createdTarget: notification.data?.url ?? null,
        notificationTitle: notification.title,
      };
      self.dispatchEvent(new NotificationEvent('notificationclick', { notification }));
      return result;
    }, { tag: NOTIFICATION_TAG, targetPath: TARGET_PATH });

    if (clickResult.createdTarget !== TARGET_PATH) {
      throw new Error(`Notification target was ${String(clickResult.createdTarget)} instead of ${TARGET_PATH}.`);
    }

    await page.waitForURL((url) => url.pathname === TARGET_PATH, { timeout: UI_TIMEOUT });
    await page.locator('.app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const pagesAfter = context.pages();
    if (pagesAfter.length !== pageCountBefore) {
      throw new Error(`Notification click opened ${pagesAfter.length - pageCountBefore} unexpected page(s).`);
    }
    if (!pagesAfter.includes(page)) {
      throw new Error('Notification click replaced the expected application page.');
    }

    report.notification = clickResult;
  });

  await step('service-worker caches remain static-only', async () => {
    const cacheAudit = await page.evaluate(async ({ runtimeCacheName }) => {
      const origin = window.location.origin;
      const cacheNames = await caches.keys();
      const entries = [];

      for (const cacheName of cacheNames) {
        const cache = await caches.open(cacheName);
        for (const request of await cache.keys()) {
          const url = new URL(request.url);
          entries.push({
            cacheName,
            url: url.href,
            pathname: url.pathname,
            origin: url.origin,
          });
        }
      }

      return {
        cacheNames,
        entryCount: entries.length,
        protectedEntries: entries.filter((entry) => entry.origin === origin
          && (entry.pathname.startsWith('/api/')
            || entry.pathname === '/login'
            || entry.pathname === '/app'
            || entry.pathname.startsWith('/app/'))),
        invalidRuntimeEntries: entries.filter((entry) => entry.cacheName === runtimeCacheName
          && (entry.origin !== origin
            || (!entry.pathname.startsWith('/assets/') && !entry.pathname.startsWith('/fonts/')))),
      };
    }, { runtimeCacheName: RUNTIME_ASSET_CACHE });

    report.cacheAudit = cacheAudit;
    if (cacheAudit.protectedEntries.length > 0) {
      throw new Error('A service-worker cache contains an API or authenticated application route.');
    }
    if (cacheAudit.invalidRuntimeEntries.length > 0) {
      throw new Error('The runtime asset cache contains a non-static or cross-origin entry.');
    }
  });

  assertNoBrowserFailures();
} catch (error) {
  suiteFailure = error;
} finally {
  if (serviceWorker) {
    await serviceWorker.evaluate(async ({ tag }) => {
      const notifications = await self.registration.getNotifications({ tag });
      for (const notification of notifications) notification.close();
    }, { tag: NOTIFICATION_TAG }).catch(() => undefined);
  }

  report.completedAt = new Date().toISOString();
  report.status = suiteFailure ? 'failed' : 'passed';
  if (suiteFailure) report.failure = serializeError(suiteFailure);

  await writeFile(
    path.join(ARTIFACT_DIR, 'report.json'),
    JSON.stringify(report, null, 2),
  );

  await context?.close().catch(() => undefined);
  await browser?.close().catch(() => undefined);
}

if (suiteFailure) throw suiteFailure;

async function step(label, action) {
  const entry = { label, startedAt: new Date().toISOString(), status: 'running' };
  report.steps.push(entry);

  try {
    const result = await action();
    entry.status = 'passed';
    entry.completedAt = new Date().toISOString();
    return result;
  } catch (error) {
    entry.status = 'failed';
    entry.completedAt = new Date().toISOString();
    entry.error = serializeError(error);
    throw error;
  }
}

function attachPageDiagnostics(targetPage) {
  targetPage.on('console', (message) => {
    if (message.type() === 'error') report.consoleErrors.push(redact(message.text()));
  });
  targetPage.on('pageerror', (error) => report.pageErrors.push(redact(error.message)));
  targetPage.on('requestfailed', (request) => {
    const entry = {
      method: request.method(),
      url: safeUrl(request.url()),
      error: redact(request.failure()?.errorText ?? 'unknown'),
    };
    report.failedRequests.push(entry);
    if (pathname(request.url()).startsWith('/api/')) report.failedApiResponses.push(entry);
  });
  targetPage.on('response', (response) => {
    if (!pathname(response.url()).startsWith('/api/') || response.status() < 400) return;
    report.failedApiResponses.push({
      method: response.request().method(),
      url: safeUrl(response.url()),
      status: response.status(),
    });
  });
}

function assertNoBrowserFailures() {
  if (report.pageErrors.length > 0) {
    throw new Error(`Browser page errors: ${JSON.stringify(report.pageErrors)}`);
  }
  if (report.consoleErrors.length > 0) {
    throw new Error(`Browser console errors: ${JSON.stringify(report.consoleErrors)}`);
  }
  if (report.failedApiResponses.length > 0) {
    throw new Error(`Failed API traffic: ${JSON.stringify(report.failedApiResponses)}`);
  }
}

function pathname(rawUrl) {
  try {
    return new URL(rawUrl).pathname;
  } catch {
    return '';
  }
}

function safeUrl(rawUrl) {
  try {
    const url = new URL(rawUrl);
    return `${url.origin}${url.pathname}`;
  } catch {
    return 'invalid-url';
  }
}

function redact(value) {
  return String(value)
    .replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, '[redacted-email]')
    .replace(/\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b/gi, '[redacted-id]')
    .replace(/Bearer\s+[A-Za-z0-9._~+/-]+=*/gi, 'Bearer [redacted-token]');
}

function serializeError(error) {
  return {
    name: error instanceof Error ? error.name : 'Error',
    message: redact(error instanceof Error ? error.message : String(error)),
    stack: error instanceof Error ? redact(error.stack ?? '') : undefined,
  };
}
