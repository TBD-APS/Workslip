import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { randomUUID } from 'node:crypto';
import { requireLoopbackOrigin, seedLocalBrowserSession } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270', 'WORKSLIP_PLAYWRIGHT_APP_URL');
const API_URL = requireLoopbackOrigin(process.env.WORKSLIP_PLAYWRIGHT_API_URL || 'http://127.0.0.1:5262', 'WORKSLIP_PLAYWRIGHT_API_URL');
const ADMIN_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL || 'admin@17v3ygzs.mailosaur.net').trim();
const UI_TIMEOUT = 25_000;
const OUT_DIR = path.join(process.env.RUNNER_TEMP || '/tmp', 'wor719-job-wizard-visual');

const { chromium } = await import('playwright');
await fs.mkdir(OUT_DIR, { recursive: true });
const browser = await chromium.launch({ headless: true });
const report = [];

const cases = [
  { name: 'day-desktop', theme: 'day', viewport: { width: 1280, height: 900 } },
  { name: 'night-desktop', theme: 'night', viewport: { width: 1280, height: 900 } },
  { name: 'day-narrow', theme: 'day', viewport: { width: 390, height: 844 } },
  { name: 'night-narrow', theme: 'night', viewport: { width: 390, height: 844 } },
];

try {
  for (const testCase of cases) {
    console.log(`[wor719] auditing ${testCase.name}`);
    const context = await browser.newContext({ viewport: testCase.viewport, locale: 'da-DK', timezoneId: 'Europe/Copenhagen' });
    const auth = await seedLocalBrowserSession(context, { appUrl: APP_URL, apiUrl: API_URL, email: ADMIN_EMAIL });
    await context.addInitScript((theme) => localStorage.setItem('theme', theme), testCase.theme);
    try {
      const jobId = await createDraft(auth.token, auth.user.userId);
      const page = await context.newPage();
      const pageErrors = [];
      page.on('pageerror', (error) => pageErrors.push(error.message));
      await page.goto(`${APP_URL}/app/job/${jobId}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
      await page.locator('.app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await page.getByRole('button', { name: 'Sagsdetaljer - aktuelt trin', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      assert.equal(await page.locator('html').getAttribute('data-theme'), testCase.theme, `${testCase.name}: theme did not apply.`);
      const palette = await resolvePalette(page);
      console.log(`[wor719] ${testCase.name} palette ${JSON.stringify(palette)}`);
      await assertBackground(page.locator('.step-dot.active'), palette.petrol, `${testCase.name}: active step must be petrol.`);
      await assertBackground(page.locator('.step-nav-btn-next:not(:disabled)'), palette.orange, `${testCase.name}: primary Next CTA must be signal orange.`);
      await assertNotBackground(page.locator('.step-nav-btn-back'), palette.orange, `${testCase.name}: Back secondary action must not use signal orange.`);

      await page.getByRole('button', { name: 'Næste', exact: true }).click();
      await page.getByRole('button', { name: 'Anlægstyper - aktuelt trin', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      const choice = page.locator('button.choice-card.selection-card').first();
      await choice.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await choice.click();
      await waitForAttribute(page, 'button.choice-card.selection-card', 'aria-pressed', 'true');
      assert.ok((await choice.getAttribute('class'))?.includes('selected'), `${testCase.name}: installation type did not commit selected class.`);
      await page.screenshot({ path: path.join(OUT_DIR, `${testCase.name}-selection-before-style-check.png`), fullPage: true });
      await assertBackground(choice, palette.petrol, `${testCase.name}: selected installation type must settle to petrol.`);
      await assertForeground(choice, palette.cream, `${testCase.name}: selected installation type foreground must use cream.`);

      const workKind = page.locator('.work-kind-option').first();
      await workKind.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      const workKindRadio = workKind.locator('input[type="radio"]');
      await workKindRadio.check();
      await page.waitForFunction(() => document.querySelector('.work-kind-option input[type="radio"]:checked')?.closest('.work-kind-option')?.classList.contains('selected') === true, undefined, { timeout: UI_TIMEOUT });
      const custom = page.getByPlaceholder('Skriv hvilken opgavetype der udføres');
      if (await custom.isVisible().catch(() => false)) await custom.fill('Palette QA');
      await assertBackground(workKind, palette.petrol, `${testCase.name}: selected work kind must settle to petrol.`);
      await assertForeground(workKind, palette.cream, `${testCase.name}: selected work kind foreground must use cream.`);
      await assertBackground(page.locator('.step-nav-btn-next:not(:disabled)'), palette.orange, `${testCase.name}: Next must remain orange beside petrol selection state.`);
      await page.screenshot({ path: path.join(OUT_DIR, `${testCase.name}-selection.png`), fullPage: true });

      await page.getByRole('button', { name: 'Næste', exact: true }).click();
      await page.getByRole('button', { name: 'Kontrolpunkter - aktuelt trin', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      const irrelevant = page.locator('.control-point-irrelevant-toggle').first();
      await irrelevant.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await assertNotBackground(irrelevant, palette.orange, `${testCase.name}: unselected Irrelevant control must not borrow primary orange.`);
      await irrelevant.click();
      await page.waitForFunction(() => document.querySelector('.control-point-irrelevant-toggle')?.getAttribute('aria-pressed') === 'true', undefined, { timeout: UI_TIMEOUT });
      assert.equal(await irrelevant.getAttribute('aria-pressed'), 'true', `${testCase.name}: Irrelevant toggle must expose selected state.`);
      await assertBackground(irrelevant, palette.petrol, `${testCase.name}: selected Irrelevant toggle must settle to petrol.`);
      await assertForeground(irrelevant, palette.cream, `${testCase.name}: selected Irrelevant toggle foreground must use cream.`);
      const completed = page.locator('.step-dot.completed').first();
      await completed.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await assertForeground(completed, palette.petrol, `${testCase.name}: completed progress icon/text must settle to petrol.`);
      await assertBackground(page.locator('.step-dot.active'), palette.petrol, `${testCase.name}: Control Points active step must remain petrol.`);
      await assertBackground(page.locator('.step-nav-btn-next:not(:disabled)'), palette.orange, `${testCase.name}: primary Next CTA must remain orange on Control Points.`);
      await assertInjectedSemanticStates(page, palette, testCase.name);
      await page.screenshot({ path: path.join(OUT_DIR, `${testCase.name}-control-points.png`), fullPage: true });
      assert.deepEqual(pageErrors, [], `${testCase.name}: browser page errors: ${pageErrors.join(' | ')}`);
      report.push({ case: testCase.name, theme: testCase.theme, viewport: testCase.viewport, verdict: 'PASS', palette, screenshots: [`${testCase.name}-selection.png`, `${testCase.name}-control-points.png`] });
    } finally { await context.close(); }
  }
  await fs.writeFile(path.join(OUT_DIR, 'report.json'), JSON.stringify(report, null, 2));
  console.log('[wor719] PASS: day/night + desktop/narrow Job Wizard palette contract verified.');
} finally { await browser.close(); }

async function createDraft(token, userId) {
  const response = await fetch(`${API_URL}/api/jobs/`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': `wor719-${randomUUID()}`, 'Content-Type': 'application/json', Accept: 'application/json' }, body: JSON.stringify({ customerId: null, customerSnapshot: { name: 'Palette QA Kunde', email: 'palette.qa@example.com', phone: '12345678', address: 'Testvej 1, 8000 Aarhus C', contactPerson: 'Palette QA' }, createCustomerFromSnapshot: false, destinationAddress: 'Testvej 1', destinationZipCode: '8000', destinationCity: 'Aarhus C', jobType: 'KLS', assignedUserIds: userId ? [userId] : [], duplicatePerAssignedUser: false, linkedJobIds: [], work: null, observations: { reportDate: null, taskDescription: 'WOR-719 palette browser verification', customerObservations: null, technicalObservations: null } }) });
  const body = await response.json().catch(() => null);
  assert.ok(response.ok, `Draft creation failed with HTTP ${response.status}: ${JSON.stringify(body)}`);
  assert.ok(body?.id, 'Draft creation response did not include id.');
  return body.id;
}

async function resolvePalette(page) {
  return page.evaluate(() => {
    const resolveBackground = (variable) => {
      const probe = document.createElement('div');
      probe.style.cssText = `position:fixed;pointer-events:none;opacity:0;background-color:var(${variable})`;
      document.body.appendChild(probe);
      const value = getComputedStyle(probe).backgroundColor;
      probe.remove();
      return value;
    };
    const resolveColor = (variable) => {
      const probe = document.createElement('div');
      probe.style.cssText = `position:fixed;pointer-events:none;opacity:0;color:var(${variable})`;
      document.body.appendChild(probe);
      const value = getComputedStyle(probe).color;
      probe.remove();
      return value;
    };
    return { orange: resolveBackground('--primary'), petrol: resolveBackground('--color-primary'), cream: resolveColor('--brand-cream') };
  });
}

async function waitForAttribute(page, selector, attribute, value) {
  await page.waitForFunction(({ selector, attribute, value }) => document.querySelector(selector)?.getAttribute(attribute) === value, { selector, attribute, value }, { timeout: UI_TIMEOUT });
}

async function waitForCss(locator, property, expected, message) {
  await locator.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const actual = await locator.evaluate(async (element, { property, expected, timeout }) => {
    const deadline = performance.now() + timeout;
    while (performance.now() < deadline) {
      const value = getComputedStyle(element)[property];
      if (value === expected) return value;
      await new Promise((resolve) => requestAnimationFrame(resolve));
    }
    return getComputedStyle(element)[property];
  }, { property, expected, timeout: UI_TIMEOUT });
  assert.equal(actual, expected, `${message} Final computed ${property} did not settle to the central token.`);
}

async function assertInjectedSemanticStates(page,palette,name){const values=await page.evaluate(()=>{const host=document.createElement('div');host.innerHTML='<button class="calendar-picker-day selected">15</button><div class="job-wizard-tutorial-progress-step is-active"><span class="job-wizard-tutorial-progress-dot"></span></div>';document.body.appendChild(host);const calendar=getComputedStyle(host.querySelector('.calendar-picker-day.selected'));const tutorial=getComputedStyle(host.querySelector('.job-wizard-tutorial-progress-dot'));const result={calendarBackground:calendar.backgroundColor,calendarColor:calendar.color,tutorialBackground:tutorial.backgroundColor};host.remove();return result;});assert.equal(values.calendarBackground,palette.petrol,`${name}: selected calendar day must be petrol.`);assert.equal(values.calendarColor,palette.cream,`${name}: selected calendar day foreground must be cream.`);assert.equal(values.tutorialBackground,palette.petrol,`${name}: active tutorial progress must be petrol.`);}
async function css(locator,property){await locator.waitFor({state:'visible',timeout:UI_TIMEOUT});return locator.evaluate((element,prop)=>getComputedStyle(element)[prop],property);}
async function assertBackground(locator,expected,message){await waitForCss(locator,'backgroundColor',expected,message);}
async function assertNotBackground(locator,forbidden,message){assert.notEqual(await css(locator,'backgroundColor'),forbidden,message);}
async function assertForeground(locator,expected,message){await waitForCss(locator,'color',expected,message);}
