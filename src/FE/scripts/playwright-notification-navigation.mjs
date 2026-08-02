import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { chromium, devices } from 'playwright';

const APP_URL = (process.env.PROD_URL ?? '').replace(/\/+$/, '');
const TARGET_PATH = '/app/profil';
const ARTIFACT_DIR = path.resolve(process.cwd(), '../../artifacts/playwright-prod-smoke');
const PAYLOAD_TAG = `playwright-wor-317-payload-${Date.now()}`;
const NAVIGATION_TAG = `playwright-wor-317-navigation-${Date.now()}`;

if (!APP_URL) throw new Error('PROD_URL is required.');

await mkdir(ARTIFACT_DIR, { recursive: true });

const report = {
  scenario: 'notification-navigation',
  appUrl: APP_URL,
  targetPath: TARGET_PATH,
  startedAt: new Date().toISOString(),
  steps: [],
};

let browser;
let context;
let page;
let worker;
let cdp;
let registrationId;
let failure;

try {
  browser = await chromium.launch();
  context = await browser.newContext({
    ...devices['iPhone 13'],
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  await context.grantPermissions(['notifications'], { origin: APP_URL });
  page = await context.newPage();

  await step('admin login', async () => {
    await page.goto(`${APP_URL}/login`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    const button = page.getByRole('button', { name: 'Dev Login · Admin', exact: true });
    await button.waitFor({ state: 'visible', timeout: 25_000 });
    await Promise.all([
      page.waitForURL((url) => url.pathname.startsWith('/app'), { timeout: 30_000 }),
      button.click(),
    ]);
  });

  await step('service worker ready', async () => {
    await page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    await page.evaluate(async () => {
      const registration = await navigator.serviceWorker.ready;
      if (!registration.active) throw new Error('No active service worker registration.');
      if (!navigator.serviceWorker.controller) {
        await new Promise((resolve) => {
          const timer = setTimeout(resolve, 5_000);
          navigator.serviceWorker.addEventListener('controllerchange', () => {
            clearTimeout(timer);
            resolve(undefined);
          }, { once: true });
        });
      }
    });

    if (!await page.evaluate(() => Boolean(navigator.serviceWorker.controller))) {
      await page.reload({ waitUntil: 'domcontentloaded', timeout: 45_000 });
    }

    worker = context.serviceWorkers().find((candidate) =>
      new URL(candidate.url()).origin === APP_URL,
    ) ?? await context.waitForEvent('serviceworker', {
      predicate: (candidate) => new URL(candidate.url()).origin === APP_URL,
      timeout: 25_000,
    });

    cdp = await context.newCDPSession(page);
    registrationId = await findRegistrationId(cdp, `${APP_URL}/`);
    report.serviceWorkerUrl = worker.url();
    report.registrationId = registrationId;
  });

  await step('real push event creates normalized notification', async () => {
    await closeNotification(worker, PAYLOAD_TAG);
    await cdp.send('ServiceWorker.deliverPushMessage', {
      origin: APP_URL,
      registrationId,
      data: JSON.stringify({
        title: '   ',
        options: {
          body: '',
          icon: 123,
          badge: null,
          tag: PAYLOAD_TAG,
          data: 'not-an-object',
        },
      }),
    });

    const audit = await waitForNotification(worker, PAYLOAD_TAG);
    if (audit.title !== 'Workslip') throw new Error(`Unexpected title: ${audit.title}`);
    if (audit.body !== 'You have a new notification') throw new Error(`Unexpected body: ${audit.body}`);
    if (!audit.icon.endsWith('/logo.png')) throw new Error(`Unexpected icon: ${audit.icon}`);
    if (!audit.badge.endsWith('/logo.png')) throw new Error(`Unexpected badge: ${audit.badge}`);
    if (audit.dataKeys.length !== 0) throw new Error(`Unsafe data retained: ${audit.dataKeys.join(',')}`);
    report.payloadAudit = audit;
    await closeNotification(worker, PAYLOAD_TAG);
  });

  await step('notification click routes existing app client', async () => {
    await page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    await closeNotification(worker, NAVIGATION_TAG);
    await cdp.send('ServiceWorker.deliverPushMessage', {
      origin: APP_URL,
      registrationId,
      data: JSON.stringify({
        title: 'Workslip Playwright',
        options: {
          body: 'WOR-317 notification navigation validation',
          tag: NAVIGATION_TAG,
          data: { url: TARGET_PATH },
        },
      }),
    });

    const created = await waitForNotification(worker, NAVIGATION_TAG);
    if (created.url !== TARGET_PATH) {
      throw new Error(`Notification target was ${String(created.url)} instead of ${TARGET_PATH}.`);
    }

    await worker.evaluate(async ({ tag }) => {
      const [notification] = await self.registration.getNotifications({ tag });
      if (!notification) throw new Error('Navigation notification disappeared before click.');
      if (typeof NotificationEvent !== 'function') {
        throw new Error('NotificationEvent is unavailable in the service worker runtime.');
      }
      self.dispatchEvent(new NotificationEvent('notificationclick', { notification }));
    }, { tag: NAVIGATION_TAG });

    await page.waitForURL((url) => url.pathname === TARGET_PATH, { timeout: 25_000 });
    report.navigation = { target: TARGET_PATH, pageCount: context.pages().length };
  });
} catch (error) {
  failure = error;
} finally {
  if (worker) {
    await closeNotification(worker, PAYLOAD_TAG).catch(() => undefined);
    await closeNotification(worker, NAVIGATION_TAG).catch(() => undefined);
  }
  report.completedAt = new Date().toISOString();
  report.status = failure ? 'failed' : 'passed';
  if (failure) report.failure = serializeError(failure);
  await writeFile(path.join(ARTIFACT_DIR, 'report.json'), JSON.stringify(report, null, 2));
  await context?.close().catch(() => undefined);
  await browser?.close().catch(() => undefined);
}

if (failure) throw failure;

async function findRegistrationId(session, expectedScope) {
  const registrations = [];
  session.on('ServiceWorker.workerRegistrationUpdated', (event) => {
    registrations.push(...event.registrations);
  });
  await session.send('ServiceWorker.enable');

  const deadline = Date.now() + 10_000;
  while (Date.now() < deadline) {
    const registration = [...registrations]
      .reverse()
      .find((candidate) => !candidate.isDeleted
        && (candidate.scopeURL === expectedScope || candidate.scopeURL.startsWith(APP_URL)));
    if (registration) return registration.registrationId;
    await new Promise((resolve) => setTimeout(resolve, 100));
  }

  throw new Error(`Could not resolve service worker registration for ${expectedScope}.`);
}

async function waitForNotification(serviceWorker, tag) {
  return serviceWorker.evaluate(async ({ expectedTag }) => {
    const deadline = Date.now() + 10_000;
    while (Date.now() < deadline) {
      const [notification] = await self.registration.getNotifications({ tag: expectedTag });
      if (notification) {
        return {
          title: notification.title,
          body: notification.body,
          icon: notification.icon,
          badge: notification.badge,
          tag: notification.tag,
          url: notification.data?.url ?? null,
          dataKeys: Object.keys(notification.data ?? {}),
        };
      }
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(`Service worker did not create notification ${expectedTag}.`);
  }, { expectedTag: tag });
}

async function closeNotification(serviceWorker, tag) {
  await serviceWorker.evaluate(async ({ expectedTag }) => {
    const notifications = await self.registration.getNotifications({ tag: expectedTag });
    for (const notification of notifications) notification.close();
  }, { expectedTag: tag });
}

async function step(label, action) {
  const entry = { label, startedAt: new Date().toISOString(), status: 'running' };
  report.steps.push(entry);
  try {
    await action();
    entry.status = 'passed';
  } catch (error) {
    entry.status = 'failed';
    entry.error = serializeError(error);
    throw error;
  } finally {
    entry.completedAt = new Date().toISOString();
  }
}

function serializeError(error) {
  return {
    name: error instanceof Error ? error.name : 'Error',
    message: error instanceof Error ? error.message : String(error),
    stack: error instanceof Error ? error.stack : undefined,
  };
}
