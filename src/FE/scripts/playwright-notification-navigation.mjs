import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { chromium, devices } from 'playwright';

const TARGET_PATH = '/app/profil';
const APP_ORIGIN = 'https://app.mrsoftware.dk';
const ARTIFACT_DIR = path.resolve(process.cwd(), '../../artifacts/playwright-notification');

await mkdir(ARTIFACT_DIR, { recursive: true });

const report = {
  scenario: 'notification-browser-contract',
  startedAt: new Date().toISOString(),
  steps: [],
};

let browser;
let context;
let page;
let failure;

try {
  browser = await chromium.launch();
  context = await browser.newContext({
    ...devices['iPhone 13'],
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  page = await context.newPage();
  await page.goto('about:blank');

  await step('browser executes shared payload normalizer', async () => {
    const source = await import('../src/pwa/pushNotificationPayload.ts');
    const normalized = source.normalizePushNotificationPayload({
      title: '   ',
      options: {
        body: '',
        icon: 123,
        badge: null,
        tag: ' job-317 ',
        data: 'not-an-object',
      },
    });

    const browserResult = await page.evaluate((value) => {
      const copy = structuredClone(value);
      return {
        title: copy.title,
        body: copy.options.body,
        icon: copy.options.icon,
        badge: copy.options.badge,
        tag: copy.options.tag,
        dataKeys: Object.keys(copy.options.data),
      };
    }, normalized);

    assertEqual(browserResult.title, 'Workslip', 'title');
    assertEqual(browserResult.body, 'You have a new notification', 'body');
    assertEqual(browserResult.icon, '/logo.png', 'icon');
    assertEqual(browserResult.badge, '/logo.png', 'badge');
    assertEqual(browserResult.tag, 'job-317', 'tag');
    assertEqual(browserResult.dataKeys.length, 0, 'data key count');
    report.payload = browserResult;
  });

  await step('browser validates safe notification target', async () => {
    const navigation = await import('../src/pwa/notificationNavigation.ts');
    const safeTarget = navigation.resolveNotificationTarget(TARGET_PATH, APP_ORIGIN);
    const unsafeTarget = navigation.resolveNotificationTarget(
      'https://example.com/phishing',
      APP_ORIGIN,
    );

    const browserResult = await page.evaluate(({ safe, unsafe }) => ({
      safePath: new URL(safe).pathname,
      unsafePath: new URL(unsafe).pathname,
      unsafeOrigin: new URL(unsafe).origin,
    }), { safe: safeTarget, unsafe: unsafeTarget });

    assertEqual(browserResult.safePath, TARGET_PATH, 'safe route');
    assertEqual(browserResult.unsafePath, '/', 'unsafe fallback route');
    assertEqual(browserResult.unsafeOrigin, APP_ORIGIN, 'unsafe fallback origin');
    report.targetResolution = browserResult;
  });

  await step('shared navigation focuses the existing app client', async () => {
    const navigation = await import('../src/pwa/notificationNavigation.ts');
    const calls = [];
    const client = {
      url: `${APP_ORIGIN}/app`,
      focused: true,
      visibilityState: 'visible',
      async navigate(url) {
        calls.push({ type: 'document-navigation', url });
        this.url = url;
        return this;
      },
      async focus() {
        calls.push({ type: 'focus', url: this.url });
        return this;
      },
    };

    const result = await navigation.navigateNotificationTarget(
      [client],
      async (url) => {
        calls.push({ type: 'open-window', url });
        return null;
      },
      TARGET_PATH,
      APP_ORIGIN,
      async (_client, url) => {
        calls.push({ type: 'router-navigation', url });
        client.url = url;
        return true;
      },
    );

    assertEqual(result, client, 'selected client');
    assertEqual(calls[0]?.type, 'router-navigation', 'first navigation mechanism');
    assertEqual(calls[0]?.url, `${APP_ORIGIN}${TARGET_PATH}`, 'router target');
    assertEqual(calls[1]?.type, 'focus', 'focus after navigation');
    if (calls.some((call) => call.type === 'open-window')) {
      throw new Error('Existing app navigation unexpectedly opened another window.');
    }
    report.navigation = calls;
  });
} catch (error) {
  failure = error;
} finally {
  report.completedAt = new Date().toISOString();
  report.status = failure ? 'failed' : 'passed';
  if (failure) report.failure = serializeError(failure);
  await writeFile(
    path.join(ARTIFACT_DIR, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  await context?.close().catch(() => undefined);
  await browser?.close().catch(() => undefined);
}

if (failure) throw failure;

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

function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}.`);
  }
}

function serializeError(error) {
  return {
    name: error instanceof Error ? error.name : 'Error',
    message: error instanceof Error ? error.message : String(error),
    stack: error instanceof Error ? error.stack : undefined,
  };
}
