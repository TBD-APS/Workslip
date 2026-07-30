import { expect, test, type Page } from '@playwright/test';
import { cpSync, rmSync } from 'node:fs';
import path from 'node:path';

test.describe.configure({ mode: 'serial' });

const baseUrl = 'http://127.0.0.1:4173/';
const root = path.resolve(process.cwd(), '.tmp-wor-213');
const coordinatorReadyEvent = 'workslip:pwa-update-coordinator-ready';
const expectedVersionBWorkerHash = process.env.WOR_213_V2_WORKER_HASH;

type RegistrationObservation = {
  installing: string | null;
  waiting: string | null;
  active: string | null;
};

function publish(version: 'dist-v1' | 'dist-v2') {
  const current = path.join(root, 'current');
  if (version === 'dist-v1') {
    rmSync(current, { recursive: true, force: true });
  }

  // Vercel keeps previously deployed immutable hashed assets available. Overlay
  // version B instead of deleting version A so the open document remains valid.
  cpSync(path.join(root, version), current, { recursive: true });
}

function attachBrowserDiagnostics(page: Page) {
  page.on('console', (message) => {
    console.log(`[browser:${message.type()}] ${message.text()}`);
  });
  page.on('pageerror', (error) => {
    console.log(`[browser:pageerror] ${error.stack ?? error.message}`);
  });
}

async function loadControlledClient(page: Page) {
  attachBrowserDiagnostics(page);
  await page.addInitScript((eventName) => {
    const testWindow = window as Window & {
      __workslipPwaCoordinatorReady?: boolean;
    };
    testWindow.__workslipPwaCoordinatorReady = false;
    window.addEventListener(eventName, () => {
      testWindow.__workslipPwaCoordinatorReady = true;
    });
  }, coordinatorReadyEvent);

  await page.goto(baseUrl, { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Log ind på Workslip' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Appopdatering' })).toHaveCount(0);

  await page.evaluate(async () => {
    await navigator.serviceWorker.ready;
  });

  if (!await page.evaluate(() => Boolean(navigator.serviceWorker.controller))) {
    await page.reload({ waitUntil: 'networkidle' });
  }

  await expect.poll(
    () => page.evaluate(() => Boolean(navigator.serviceWorker.controller)),
    { timeout: 10_000 },
  ).toBe(true);

  await expect.poll(
    () => page.evaluate(() => Boolean(
      (window as Window & { __workslipPwaCoordinatorReady?: boolean })
        .__workslipPwaCoordinatorReady,
    )),
    { timeout: 10_000 },
  ).toBe(true);
}

async function currentAppAsset(page: Page) {
  return page.locator('script[type="module"][src*="/assets/app-"]').getAttribute('src');
}

async function readRegistrationObservation(page: Page) {
  return page.evaluate(async (): Promise<RegistrationObservation> => {
    const registration = await navigator.serviceWorker.getRegistration();
    if (!registration) throw new Error('No service-worker registration found.');

    return {
      installing: registration.installing?.state ?? null,
      waiting: registration.waiting?.state ?? null,
      active: registration.active?.state ?? null,
    };
  });
}

async function publishAndDiscoverUpdate(page: Page) {
  publish('dist-v2');

  const initialObservation = await page.evaluate(async () => {
    const registration = await navigator.serviceWorker.getRegistration();
    if (!registration) throw new Error('No service-worker registration found.');

    registration.addEventListener('updatefound', () => {
      console.log('[WOR-213 validation] updatefound');
      const installingWorker = registration.installing;
      console.log(`[WOR-213 validation] installing=${installingWorker?.state ?? 'none'}`);
      installingWorker?.addEventListener('statechange', () => {
        console.log(`[WOR-213 validation] installing state=${installingWorker.state}`);
      });
    });

    const response = await fetch(registration.active?.scriptURL ?? '/sw.js', {
      cache: 'no-store',
    });
    const publishedWorker = await response.text();
    const workerDigest = await crypto.subtle.digest(
      'SHA-256',
      new TextEncoder().encode(publishedWorker),
    );
    const publishedWorkerHash = Array.from(new Uint8Array(workerDigest))
      .map((byte) => byte.toString(16).padStart(2, '0'))
      .join('');

    // Exercise Workslip's actual online/refocus update path rather than calling
    // registration.update() outside the application coordinator.
    window.dispatchEvent(new Event('online'));

    return {
      installing: registration.installing?.state ?? null,
      waiting: registration.waiting?.state ?? null,
      active: registration.active?.state ?? null,
      publishedWorkerLength: publishedWorker.length,
      publishedWorkerHash,
    };
  });

  console.log(`Initial service-worker observation: ${JSON.stringify(initialObservation)}`);
  expect(expectedVersionBWorkerHash).toBeTruthy();
  expect(initialObservation.publishedWorkerHash).toBe(expectedVersionBWorkerHash);
  await expect(page.getByRole('button', { name: 'Opdater nu' })).toBeVisible({ timeout: 8_000 });

  const readyObservation = await readRegistrationObservation(page);
  console.log(`Ready service-worker observation: ${JSON.stringify(readyObservation)}`);
  expect(readyObservation.waiting).not.toBeNull();
}

test('button applies one update without leaving the page frozen', async ({ browser }) => {
  publish('dist-v1');
  const context = await browser.newContext();
  const page = await context.newPage();
  await loadControlledClient(page);

  const previousAsset = await currentAppAsset(page);
  expect(previousAsset).toBeTruthy();

  let updateNavigations = 0;
  page.on('framenavigated', (frame) => {
    if (frame === page.mainFrame()) updateNavigations += 1;
  });

  await publishAndDiscoverUpdate(page);
  await page.getByRole('button', { name: 'Opdater nu' }).click();
  await expect(page.getByRole('button', { name: 'Opdaterer...' })).toBeDisabled();

  await expect.poll(async () => {
    try {
      return await currentAppAsset(page);
    } catch {
      return null;
    }
  }, { timeout: 15_000 }).not.toBe(previousAsset);

  await expect(page.getByRole('heading', { name: 'Log ind på Workslip' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Appopdatering' })).toHaveCount(0);
  await page.waitForTimeout(2_000);
  expect(updateNavigations).toBe(1);
  await context.close();
});

test('untouched banner applies the same update automatically once', async ({ browser }) => {
  publish('dist-v1');
  const context = await browser.newContext();
  const page = await context.newPage();
  await loadControlledClient(page);

  const previousAsset = await currentAppAsset(page);
  expect(previousAsset).toBeTruthy();

  let updateNavigations = 0;
  page.on('framenavigated', (frame) => {
    if (frame === page.mainFrame()) updateNavigations += 1;
  });

  await publishAndDiscoverUpdate(page);
  await expect.poll(async () => {
    try {
      return await currentAppAsset(page);
    } catch {
      return null;
    }
  }, { timeout: 20_000 }).not.toBe(previousAsset);

  await expect(page.getByRole('heading', { name: 'Log ind på Workslip' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Appopdatering' })).toHaveCount(0);
  await page.waitForTimeout(2_000);
  expect(updateNavigations).toBe(1);
  await context.close();
});
