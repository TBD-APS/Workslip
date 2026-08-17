import { mkdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

const APP_URL = requireLoopbackOrigin(process.env.WORKSLIP_LOCAL_APP_URL, 'WORKSLIP_LOCAL_APP_URL');
const API_URL = requireLoopbackOrigin(process.env.WORKSLIP_LOCAL_API_URL, 'WORKSLIP_LOCAL_API_URL');
const ADMIN_EMAIL = requireValue(process.env.WORKSLIP_SYNTHETIC_ADMIN_EMAIL, 'WORKSLIP_SYNTHETIC_ADMIN_EMAIL').toLowerCase();
const UI_TIMEOUT = 25_000;
const API_TIMEOUT = 30_000;
const artifactDir = path.resolve(process.cwd(), '../../artifacts/playwright-job-status-dots');

const expected = [
  ['job-status-dot--draft', 'rgb(59, 130, 246)'],
  ['job-status-dot--in-review', 'rgb(234, 179, 8)'],
  ['job-status-dot--approved', 'rgb(34, 197, 94)'],
  ['job-status-dot--rejected', 'rgb(239, 68, 68)'],
];

await rm(artifactDir, { recursive: true, force: true });
await mkdir(artifactDir, { recursive: true });
const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });
const report = {
  scenario: 'job-wizard',
  target: 'isolated-local-actions',
  startedAt: new Date().toISOString(),
  exactProductHead: process.env.WORKSLIP_PRODUCT_HEAD ?? null,
  runs: [],
};

let failure = null;
try {
  const tokenPayload = await requestDevToken();
  const jobsResponse = await fetch(`${API_URL}/api/jobs/?limit=200`, {
    headers: { Authorization: `Bearer ${tokenPayload.token}`, Accept: 'application/json' },
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  if (!jobsResponse.ok) throw new Error(`GET /api/jobs returned HTTP ${jobsResponse.status}.`);
  const jobsPayload = await jobsResponse.json();
  const jobs = Array.isArray(jobsPayload?.items) ? jobsPayload.items : Array.isArray(jobsPayload) ? jobsPayload : [];
  const editable = jobs.find((job) => {
    const status = String(job?.status ?? '').toLowerCase();
    return status === 'draft' || status === '0' || status === 'reopened' || status === '4' || status === 'rejected' || status === '3';
  });
  if (!editable?.id) throw new Error('Synthetic seed contains no editable Draft/Reopened/Rejected job for JobWizard evidence.');

  await runViewport('desktop-1280x800', { viewport: { width: 1280, height: 800 } }, editable.id);
  await runViewport('mobile-iphone-13', devices['iPhone 13'], editable.id);
} catch (error) {
  failure = error;
  report.failure = serialize(error);
} finally {
  report.completedAt = new Date().toISOString();
  report.status = failure ? 'failed' : 'passed';
  await writeFile(path.join(artifactDir, 'report.json'), JSON.stringify(report, null, 2));
  await browser.close();
}

if (failure) throw failure;
console.log('[job-status-dots] desktop + iPhone evidence passed.');

async function runViewport(name, contextOptions, jobId) {
  const context = await browser.newContext({ ...contextOptions, locale: 'da-DK', timezoneId: 'Europe/Copenhagen' });
  const page = await context.newPage();
  const run = { name, pageErrors: [], consoleErrors: [], colors: {}, status: 'running' };
  report.runs.push(run);

  page.on('pageerror', (error) => run.pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') run.consoleErrors.push(message.text());
  });

  try {
    await page.goto(`${APP_URL}/login`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    const devLogin = page.getByRole('button', { name: 'Dev Login · Admin', exact: true });
    await devLogin.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await devLogin.click();
    await page.waitForURL((url) => url.pathname.startsWith('/app'), { timeout: API_TIMEOUT });

    await page.goto(`${APP_URL}/app/job/${jobId}`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    const dots = page.locator('.job-status-dots .job-status-dot');
    await dots.first().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const count = await dots.count();
    if (count !== 4) throw new Error(`${name}: expected exactly 4 status dots, found ${count}.`);
    if (await page.getByRole('button', { name: /Genåbnet/ }).count()) throw new Error(`${name}: Genåbnet rendered as a fifth status dot.`);

    for (const [className, rgb] of expected) {
      const dot = page.locator(`.${className}`).first();
      if ((await dot.count()) !== 1) throw new Error(`${name}: expected one .${className} dot.`);
      const actual = await dot.evaluate((element) => ({
        color: getComputedStyle(element).color,
        pseudoBackground: getComputedStyle(element, '::before').backgroundColor,
      }));
      run.colors[className] = actual;
      if (actual.color !== rgb || actual.pseudoBackground !== rgb) {
        throw new Error(`${name}: .${className} expected ${rgb}; got color=${actual.color}, pseudo=${actual.pseudoBackground}.`);
      }
    }

    const overflow = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }));
    run.overflow = overflow;
    if (overflow.scrollWidth > overflow.clientWidth) {
      throw new Error(`${name}: horizontal page overflow ${overflow.scrollWidth}px > ${overflow.clientWidth}px.`);
    }

    await page.locator('.job-status-dots').screenshot({ path: path.join(artifactDir, `${name}.png`) });
    await page.waitForTimeout(250);
    if (run.pageErrors.length) throw new Error(`${name}: page errors: ${run.pageErrors.join(' | ')}`);
    if (run.consoleErrors.length) throw new Error(`${name}: console errors: ${run.consoleErrors.join(' | ')}`);
    run.status = 'passed';
  } catch (error) {
    run.status = 'failed';
    run.failure = serialize(error);
    throw error;
  } finally {
    await context.close();
  }
}

async function requestDevToken() {
  const response = await fetch(`${API_URL}/api/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email: ADMIN_EMAIL }),
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok || !payload?.token) throw new Error(`Development token returned HTTP ${response.status}.`);
  return payload;
}

function requireLoopbackOrigin(value, name) {
  const normalized = requireValue(value, name);
  let url;
  try { url = new URL(normalized); } catch { throw new Error(`${name} must be a loopback HTTP origin.`); }
  if (url.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) || url.username || url.password || url.search || url.hash || !['', '/'].includes(url.pathname)) {
    throw new Error(`${name} must be a loopback HTTP origin without credentials/path/query/fragment.`);
  }
  return url.origin;
}

function requireValue(value, name) {
  const normalized = String(value ?? '').trim();
  if (!normalized) throw new Error(`${name} is required.`);
  return normalized;
}

function serialize(error) {
  return { name: error?.name ?? 'Error', message: error?.message ?? String(error), stack: error?.stack ?? null };
}
