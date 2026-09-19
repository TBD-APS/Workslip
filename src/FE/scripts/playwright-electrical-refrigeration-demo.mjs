import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from 'playwright';
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
const EVIDENCE_DIR = process.env.WORKSLIP_PLAYWRIGHT_EVIDENCE_DIR?.trim() || '';
const UI_TIMEOUT = 20_000;

async function screenshot(page, filename) {
  if (!EVIDENCE_DIR) return;
  await mkdir(EVIDENCE_DIR, { recursive: true });
  await page.screenshot({ path: path.join(EVIDENCE_DIR, filename), fullPage: true });
}

async function run() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    viewport: { width: 1440, height: 1000 },
  });

  try {
    const session = await seedLocalBrowserSession(context, {
      appUrl: APP_URL,
      apiUrl: API_URL,
      email: ADMIN_EMAIL,
    });
    const page = await context.newPage();

    await page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: 'Opgaver', exact: true }).waitFor({ timeout: UI_TIMEOUT });
    const referenceResponse = await context.request.get(`${API_URL}/api/reference-data`, {
      headers: { Authorization: `Bearer ${session.token}` },
    });
    assert.equal(referenceResponse.ok(), true, `Referencedata returnerede HTTP ${referenceResponse.status()}.`);
    const referenceData = await referenceResponse.json();
    assert.deepEqual(
      referenceData.installationTypes.map((installationType) => installationType.name).sort(),
      ['EL', 'KØL'],
      'Den isolerede browserdatabase skal eksponere EL/KØL-profilen.',
    );

    await page.locator('#app-fab-create-job').click();
    const dialog = page.locator('#electrical-refrigeration-create-dialog');
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.locator('#er-create-step-foundation').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await screenshot(page, 'jh-el-koel-01-foundation.png');

    await page.getByRole('button', { name: 'Vælg kunde...', exact: true }).click();
    await page.getByRole('option', { name: /Opret ny kunde/ }).click();
    await page.locator('#job-customer-name').fill('Nordhavn Produktion A/S');
    await page.locator('#job-customer-email').fill('drift@nordhavn-produktion.dk');
    await page.locator('#job-customer-phone').fill('70112233');
    await page.locator('#er-asset-name').fill('Varmepumpe VP-04');
    await page.locator('#er-asset-model').fill('Panasonic Aquarea T-CAP');
    await page.locator('#er-asset-serial-number').fill('VP04-2026-1842');
    await page.locator('#er-task-description').fill('Udskift varmepumpe og etablér ny elforsyning.');

    const next = page.locator('#er-create-next');
    await assertEnabled(next, 'Grundlag skal kunne fortsætte efter gyldige stamdata');
    await next.click();

    await page.locator('#er-create-step-disciplines').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.locator('#er-track-combined').click();
    await assertEnabled(next, 'EL + KØL skal vælge en gyldig standard-opgavetype');
    await next.click();

    await page.locator('#er-create-step-staffing').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const electricalEmployee = page.locator('#er-employee-a1a1a1a1-da5b-4cc4-bbeb-07b40cab806f');
    const refrigerationEmployee = page.locator('#er-employee-b2b2b2b2-da5b-4cc4-bbeb-07b40cab806f');

    if ((await electricalEmployee.getAttribute('aria-pressed')) !== 'true') {
      await electricalEmployee.click();
    }
    await page.locator('#er-missing-competencies').getByText(/KØL/).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await next.isDisabled(), true, 'Kombinationssagen må ikke fortsætte uden KØL-kompetence.');

    await refrigerationEmployee.click();
    await page.locator('#er-missing-competencies').waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await assertEnabled(next, 'Kompetence-gaten skal åbne, når både EL og KØL er dækket');
    await next.click();

    await page.locator('#er-create-step-review').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await dialog.getByText('EL + KØL', { exact: true }).first().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await dialog.getByText('Lars Holm + Mikkel Sørensen', { exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await screenshot(page, 'jh-el-koel-02-review.png');

    const createResponsePromise = page.waitForResponse((response) => (
      response.request().method() === 'POST'
      && ['/api/jobs', '/api/jobs/'].includes(new URL(response.url()).pathname)
    ), { timeout: UI_TIMEOUT });
    await page.locator('#er-create-submit').click();
    const createResponse = await createResponsePromise;
    assert.equal(createResponse.ok(), true, `Sagsoprettelse returnerede HTTP ${createResponse.status()}.`);
    const created = await createResponse.json();
    assert.ok(created?.id, 'Sagsoprettelsen skal returnere et sags-id.');

    await page.waitForURL((url) => url.pathname === `/app/job/${created.id}`, { timeout: UI_TIMEOUT });
    const persistedResponse = await context.request.get(`${API_URL}/api/jobs/${created.id}`, {
      headers: { Authorization: `Bearer ${session.token}` },
    });
    assert.equal(persistedResponse.ok(), true, `Den oprettede sag kunne ikke genindlæses: HTTP ${persistedResponse.status()}.`);
    const persisted = await persistedResponse.json();
    assert.deepEqual(
      persisted.work.installationTypes.map((installationType) => installationType.name).sort(),
      ['EL', 'KØL'],
      'Kombinationssagen skal gemme begge fagspor.',
    );
    assert.deepEqual(
      persisted.assignedUsers.map((user) => user.displayName).sort(),
      ['Lars Holm', 'Mikkel Sørensen'],
      'Kombinationssagen skal gemme begge kompetencedækkende medarbejdere.',
    );
    assert.match(persisted.observations.technicalObservations, /Fagspor: EL \+ KØL/);
    assert.match(persisted.observations.technicalObservations, /Fagligt ansvarlig EL/);
    assert.match(persisted.observations.technicalObservations, /KMO A2/);

    console.log(`[test] JH EL/KØL demo flow created and persisted job ${created.id}.`);
  } finally {
    await browser.close();
  }
}

async function assertEnabled(locator, message) {
  await locator.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  assert.equal(await locator.isEnabled(), true, message);
}

await run();
