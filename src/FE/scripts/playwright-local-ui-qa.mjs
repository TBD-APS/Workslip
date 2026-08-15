import fs from 'node:fs/promises';
import path from 'node:path';
import { chromium } from './node_modules/playwright/index.mjs';

const frontendUrl = 'http://127.0.0.1:5270';
const backendUrl = 'http://localhost:5262';
const devEmail = 'admin@17v3ygzs.mailosaur.net';
const rounds = Number(process.env.WORKSLIP_QA_ROUNDS ?? 3);
const evidenceRoot = path.resolve('artifacts', 'local-ui-qa');
const profiles = [
  { name: 'desktop', viewport: { width: 1440, height: 1000 }, isMobile: false },
  { name: 'mobile', viewport: { width: 390, height: 844 }, isMobile: true },
];
const themes = ['day', 'night'];
const statusCases = [
  { label: 'Aktive sager', status: 'Draft', filterLabel: 'Aktiv' },
  { label: 'Til gennemsyn', status: 'InReview', filterLabel: 'Til gennemsyn' },
  { label: 'Godkendte sager', status: 'Approved', filterLabel: 'Godkendt' },
];
const assert = (value, message) => { if (!value) throw new Error(message); };

async function token() {
  const response = await fetch(`${backendUrl}/api/dev/token`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: devEmail }) });
  assert(response.ok, `dev token HTTP ${response.status}`);
  return (await response.json()).token;
}
async function overviewApi(authToken) {
  const response = await fetch(`${backendUrl}/api/jobs/overview`, { headers: { authorization: `Bearer ${authToken}` } });
  assert(response.ok, `overview HTTP ${response.status}`);
  const data = await response.json();
  const dates = data.recentJobs.map((job) => Date.parse(job.updatedAt));
  for (let i = 1; i < dates.length; i += 1) assert(dates[i - 1] >= dates[i], 'recent jobs are not updatedAt desc');
  return data;
}
async function openOverview(page) {
  await page.goto(`${frontendUrl}/app/overblik`, { waitUntil: 'networkidle' });
  await page.getByRole('heading', { name: 'Overblik' }).waitFor();
}
async function verifyStatusNavigation(page) {
  for (const item of statusCases) {
    await openOverview(page);
    await page.getByRole('button', { name: new RegExp(item.label, 'i') }).click();
    await page.waitForURL((url) => url.pathname === '/app' && url.searchParams.get('status') === item.status);
    const filter = page.getByRole('button', { name: item.filterLabel, exact: true });
    await filter.waitFor();
    assert((await filter.getAttribute('aria-pressed')) === 'true', `${item.label} did not activate filter`);
  }
  await openOverview(page);
  await page.getByRole('button', { name: /Se afviste sager/i }).click();
  await page.waitForURL((url) => url.pathname === '/app' && url.searchParams.get('status') === 'Rejected');
  assert((await page.getByRole('button', { name: 'Afvist', exact: true }).getAttribute('aria-pressed')) === 'true', 'Rejected filter not selected');
}
async function verifySearch(page) {
  await openOverview(page);
  await page.getByRole('button', { name: 'Hurtig navigation' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Find en sag' });
  await dialog.waitFor();
  const input = dialog.getByRole('searchbox', { name: 'Søg i Workslip' });
  await input.fill('Vestergade');
  await page.waitForTimeout(350);
  assert(await dialog.isVisible(), 'search dialog closed unexpectedly');
  assert(!(await dialog.innerText()).includes('Åbn intern viden og dokumentation'), 'Docs exposed in primary search');
  await page.keyboard.press('Escape');
}
async function verifyAreas(page) {
  for (const url of ['/app/timer', '/app/users', '/app/customers']) {
    await page.goto(`${frontendUrl}${url}`, { waitUntil: 'networkidle' });
    assert(new URL(page.url()).pathname === url, `wrong route for ${url}`);
  }
}

await fs.rm(evidenceRoot, { recursive: true, force: true });
const authToken = await token();
const api = await overviewApi(authToken);
const browser = await chromium.launch({ headless: true });
const results = [];
try {
  for (let round = 1; round <= rounds; round += 1) {
    for (const profile of profiles) {
      for (const theme of themes) {
        const context = await browser.newContext({ viewport: profile.viewport, isMobile: profile.isMobile, hasTouch: profile.isMobile });
        const page = await context.newPage();
        const errors = [];
        page.on('console', (m) => { if (m.type() === 'error' && !m.text().includes('favicon')) errors.push(m.text()); });
        page.on('pageerror', (e) => errors.push(e.message));
        await page.addInitScript(({ authToken: t, selectedTheme, email }) => { localStorage.setItem('authToken', t); localStorage.setItem('userEmail', email); localStorage.setItem('theme', selectedTheme); }, { authToken, selectedTheme: theme, email: devEmail });
        await openOverview(page);
        assert((await page.locator('html').getAttribute('data-theme')) === theme, 'theme not applied');
        assert((await page.getByRole('link', { name: 'Docs', exact: true }).count()) === 0, 'Docs in primary nav');
        if (api.recentJobs.length) {
          const text = await page.locator('.overview-recent-row').first().innerText();
          assert(text.includes(api.recentJobs[0].customerName || 'Kunde ikke angivet'), 'recent customer mismatch');
          if (api.recentJobs[0].customerNumber) assert(text.includes(api.recentJobs[0].customerNumber), 'customer number missing');
        }
        await verifyStatusNavigation(page);
        await verifySearch(page);
        await verifyAreas(page);
        await openOverview(page);
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        assert(overflow <= 1, `horizontal overflow ${overflow}`);
        const destination = path.join(evidenceRoot, `round-${round}`, `${profile.name}-${theme}.png`);
        await fs.mkdir(path.dirname(destination), { recursive: true });
        await page.screenshot({ path: destination, fullPage: true });
        assert(errors.length === 0, errors.join(' | '));
        results.push({ round, profile: profile.name, theme, screenshot: destination });
        console.log(`[OK] round=${round} viewport=${profile.name} theme=${theme}`);
        await context.close();
      }
    }
  }
} finally { await browser.close(); }
await fs.writeFile(path.join(evidenceRoot, 'report.json'), JSON.stringify({ rounds, results }, null, 2));
console.log(`[OK] ${results.length} browser/theme passes completed`);
