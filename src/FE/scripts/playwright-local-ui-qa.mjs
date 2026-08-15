import fs from 'node:fs/promises';
import path from 'node:path';
import { chromium } from './node_modules/playwright/index.mjs';

const frontendUrl = (process.env.WORKSLIP_LOCAL_FRONTEND ?? 'http://127.0.0.1:5270').replace(/\/$/, '');
const backendUrl = (process.env.WORKSLIP_LOCAL_BACKEND ?? 'http://localhost:5262').replace(/\/$/, '');
const devEmail = process.env.WORKSLIP_LOCAL_EMAIL ?? 'admin@17v3ygzs.mailosaur.net';
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

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function getDevToken() {
  const response = await fetch(`${backendUrl}/api/dev/token`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email: devEmail }),
  });
  assert(response.ok, `Dev token failed: HTTP ${response.status}`);
  const payload = await response.json();
  assert(payload.token, 'Dev token response did not contain token');
  return payload.token;
}

async function verifyOverviewApi(token) {
  const response = await fetch(`${backendUrl}/api/jobs/overview`, {
    headers: { authorization: `Bearer ${token}` },
  });
  assert(response.ok, `Overview API failed: HTTP ${response.status}`);
  const payload = await response.json();
  for (const key of ['activeCount', 'inReviewCount', 'approvedCount', 'rejectedCount', 'recentJobs']) {
    assert(Object.hasOwn(payload, key), `Overview API missing ${key}`);
  }
  const updated = payload.recentJobs.map((job) => Date.parse(job.updatedAt));
  for (let index = 1; index < updated.length; index += 1) {
    assert(updated[index - 1] >= updated[index], 'Recent jobs are not sorted by updatedAt desc');
  }
  return payload;
}

async function assertNoHorizontalOverflow(page, scope) {
  const overflow = await page.evaluate(() => ({
    body: document.body.scrollWidth - document.body.clientWidth,
    html: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  }));
  assert(overflow.body <= 1 && overflow.html <= 1, `${scope}: horizontal overflow detected (${JSON.stringify(overflow)})`);
}

async function assertNoFatalUi(page, scope) {
  const text = await page.locator('body').innerText();
  const forbidden = [
    'Noget gik galt',
    'Kunne ikke hente overblikket',
    'Forbindelsen tager længere tid end normalt',
  ];
  for (const phrase of forbidden) {
    assert(!text.includes(phrase), `${scope}: fatal UI state contains '${phrase}'`);
  }
}

async function openOverview(page) {
  await page.goto(`${frontendUrl}/app/overblik`, { waitUntil: 'networkidle' });
  await page.getByRole('heading', { name: 'Overblik' }).waitFor();
}

async function verifyNavigation(page) {
  for (const testCase of statusCases) {
    await openOverview(page);
    await page.getByRole('button', { name: new RegExp(testCase.label, 'i') }).click();
    await page.waitForURL((url) => url.pathname === '/app' && url.searchParams.get('status') === testCase.status);
    const selected = page.getByRole('button', { name: testCase.filterLabel, exact: true });
    await selected.waitFor();
    assert((await selected.getAttribute('aria-pressed')) === 'true', `${testCase.label}: target filter is not selected`);
  }

  await openOverview(page);
  await page.getByRole('button', { name: /Se afviste sager/i }).click();
  await page.waitForURL((url) => url.pathname === '/app' && url.searchParams.get('status') === 'Rejected');
  const rejected = page.getByRole('button', { name: 'Afvist', exact: true });
  await rejected.waitFor();
  assert((await rejected.getAttribute('aria-pressed')) === 'true', 'Rejected filter is not selected');
}

async function verifyPrimaryAreas(page) {
  const destinations = [
    { label: 'Timer', path: '/app/timer' },
    { label: 'Folk', path: '/app/users' },
    { label: 'Kunder', path: '/app/customers' },
  ];

  for (const destination of destinations) {
    await page.goto(`${frontendUrl}${destination.path}`, { waitUntil: 'networkidle' });
    assert(page.url().includes(destination.path), `${destination.label}: wrong destination URL`);
    await assertNoFatalUi(page, destination.label);
  }
}

async function verifySearch(page) {
  await openOverview(page);
  const trigger = page.getByRole('button', { name: 'Hurtig navigation' }).first();
  await trigger.click();
  const dialog = page.getByRole('dialog', { name: 'Hvor vil du hen?' });
  await dialog.waitFor();
  const input = dialog.getByRole('searchbox', { name: 'Søg i Workslip' });
  await input.fill('sag');
  await page.waitForTimeout(350);
  assert(await dialog.isVisible(), 'Search dialog closed unexpectedly');
  assert(!(await dialog.innerText()).includes('Åbn intern viden og dokumentation'), 'Docs is still exposed in the primary search commands');
  await page.keyboard.press('Escape');
}

async function runProfile(browser, token, round, profile, theme, overviewApi) {
  const context = await browser.newContext({
    viewport: profile.viewport,
    isMobile: profile.isMobile,
    hasTouch: profile.isMobile,
  });
  const consoleErrors = [];
  const page = await context.newPage();
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => consoleErrors.push(error.message));

  await page.addInitScript(({ authToken, selectedTheme, email }) => {
    localStorage.setItem('authToken', authToken);
    localStorage.setItem('userEmail', email);
    localStorage.setItem('theme', selectedTheme);
  }, { authToken: token, selectedTheme: theme, email: devEmail });

  const scope = `round ${round} / ${profile.name} / ${theme}`;
  await openOverview(page);
  assert((await page.locator('html').getAttribute('data-theme')) === theme, `${scope}: theme was not applied`);
  await assertNoHorizontalOverflow(page, scope);
  await assertNoFatalUi(page, scope);
  await page.getByRole('button', { name: /Aktive sager/i }).waitFor();
  await page.getByRole('button', { name: /Til gennemsyn/i }).waitFor();
  await page.getByRole('button', { name: /Godkendte sager/i }).waitFor();
  await page.getByRole('button', { name: /Se afviste sager/i }).waitFor();

  const docsPrimary = page.getByRole('link', { name: 'Docs', exact: true });
  assert((await docsPrimary.count()) === 0, `${scope}: Docs is still present in primary navigation`);

  const recentRows = page.locator('.overview-recent-row:not(.overview-recent-row--skeleton)');
  if (overviewApi.recentJobs.length > 0) {
    await recentRows.first().waitFor();
    const firstRowText = await recentRows.first().innerText();
    assert(firstRowText.includes('SAG-'), `${scope}: recent job does not show case number`);
    assert(firstRowText.includes(overviewApi.recentJobs[0].customerName || 'Kunde ikke angivet'), `${scope}: recent job customer does not match backend`);
    if (overviewApi.recentJobs[0].customerNumber) {
      assert(firstRowText.includes(overviewApi.recentJobs[0].customerNumber), `${scope}: customer number missing in recent job`);
    }
  }

  await verifyNavigation(page);
  await verifySearch(page);
  await verifyPrimaryAreas(page);
  await openOverview(page);
  await assertNoHorizontalOverflow(page, scope);

  const destination = path.join(evidenceRoot, `round-${round}`, `${profile.name}-${theme}.png`);
  await fs.mkdir(path.dirname(destination), { recursive: true });
  await page.screenshot({ path: destination, fullPage: true });

  const meaningfulErrors = consoleErrors.filter((entry) => !entry.includes('favicon'));
  assert(meaningfulErrors.length === 0, `${scope}: browser console errors: ${meaningfulErrors.join(' | ')}`);
  await context.close();
  return destination;
}

await fs.rm(evidenceRoot, { recursive: true, force: true });
const token = await getDevToken();
const overviewApi = await verifyOverviewApi(token);
const browser = await chromium.launch({ headless: true });
const results = [];
try {
  for (let round = 1; round <= rounds; round += 1) {
    for (const profile of profiles) {
      for (const theme of themes) {
        const screenshot = await runProfile(browser, token, round, profile, theme, overviewApi);
        results.push({ round, profile: profile.name, theme, screenshot });
        console.log(`[OK] round=${round} viewport=${profile.name} theme=${theme}`);
      }
    }
  }
} finally {
  await browser.close();
}

await fs.writeFile(
  path.join(evidenceRoot, 'report.json'),
  JSON.stringify({ rounds, frontendUrl, backendUrl, results }, null, 2),
  'utf8',
);
console.log(`[OK] ${results.length} browser/theme passes completed. Evidence: ${evidenceRoot}`);
