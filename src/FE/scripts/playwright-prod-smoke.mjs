import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { chromium, devices } from 'playwright';

const baseUrl = (process.env.PROD_URL ?? '').replace(/\/+$/, '');
const scenario = process.env.SCENARIO ?? 'public-smoke';
const artifactDir = path.resolve(process.cwd(), '../../artifacts/playwright-prod-smoke');
const viewportName = 'iPhone 13';
const startedAt = new Date();
const runStamp = startedAt.toISOString().replace(/[-:.TZ]/g, '').slice(0, 14);
const customerName = `PLAYWRIGHT ${runStamp}`;
const destinationAddress = 'Testvej 1, 8000 Aarhus C';

if (!baseUrl) {
  throw new Error('PROD_URL is required.');
}
if (!['public-smoke', 'full-case'].includes(scenario)) {
  throw new Error(`Unsupported scenario: ${scenario}`);
}

await mkdir(artifactDir, { recursive: true });

const report = {
  scenario,
  baseUrl,
  startedAt: startedAt.toISOString(),
  viewport: devices[viewportName].viewport,
  customerName: scenario === 'full-case' ? customerName : null,
  jobId: null,
  reportNumber: null,
  finalStatus: null,
  steps: [],
  consoleErrors: [],
  pageErrors: [],
  failedRequests: [],
  failedApiResponses: [],
  traceIncluded: scenario === 'public-smoke',
};

const browser = await chromium.launch();
const context = await browser.newContext({
  ...devices[viewportName],
});
const page = await context.newPage();
let trackAuthenticatedApiFailures = false;
let failure = null;
let traceStarted = false;

page.on('console', (message) => {
  if (message.type() === 'error') {
    report.consoleErrors.push(redact(message.text()));
  }
});
page.on('pageerror', (error) => {
  report.pageErrors.push(redact(error.message));
});
page.on('requestfailed', (request) => {
  const entry = {
    method: request.method(),
    url: safeUrl(request.url()),
    error: redact(request.failure()?.errorText ?? 'unknown'),
  };
  report.failedRequests.push(entry);
  if (trackAuthenticatedApiFailures && request.url().includes('/api/')) {
    report.failedApiResponses.push(entry);
  }
});
page.on('response', (response) => {
  if (!trackAuthenticatedApiFailures || !response.url().includes('/api/') || response.status() < 400) {
    return;
  }
  report.failedApiResponses.push({
    method: response.request().method(),
    url: safeUrl(response.url()),
    status: response.status(),
  });
});

function redact(value) {
  return String(value)
    .replace(/Bearer\s+[^\s,;]+/gi, 'Bearer [REDACTED]')
    .replace(/\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b/g, '[REDACTED_TOKEN]');
}

function safeUrl(value) {
  try {
    const url = new URL(value);
    url.search = '';
    url.hash = '';
    return url.toString();
  } catch {
    return redact(value);
  }
}

function fileSafe(value) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

async function screenshot(name) {
  await page.screenshot({
    path: path.join(artifactDir, `${fileSafe(name)}.png`),
    fullPage: true,
  });
}

async function runStep(name, action, { capture = true } = {}) {
  const step = { name, startedAt: new Date().toISOString(), status: 'running' };
  report.steps.push(step);
  try {
    const result = await action();
    step.status = 'passed';
    step.completedAt = new Date().toISOString();
    if (capture) await screenshot(name);
    return result;
  } catch (error) {
    step.status = 'failed';
    step.completedAt = new Date().toISOString();
    step.error = redact(error instanceof Error ? error.message : String(error));
    try {
      await screenshot(`${name}-failed`);
    } catch {
      // Preserve the original failure.
    }
    throw error;
  }
}

async function waitForEnabled(locator, description, timeout = 20_000) {
  await locator.waitFor({ state: 'visible', timeout });
  const deadline = Date.now() + timeout;
  while (await locator.isDisabled()) {
    if (Date.now() > deadline) {
      throw new Error(`${description} remained disabled.`);
    }
    await page.waitForTimeout(200);
  }
}

async function expectOk(response, description) {
  if (!response) throw new Error(`${description} returned no HTTP response.`);
  if (!response.ok()) throw new Error(`${description} returned HTTP ${response.status()}.`);
  return response;
}

async function waitForStep(label) {
  await page.getByRole('button', { name: `${label} - aktuelt trin`, exact: true })
    .waitFor({ state: 'visible', timeout: 20_000 });
}

async function clickNext(nextStepLabel) {
  const next = page.getByRole('button', { name: 'Næste', exact: true });
  await waitForEnabled(next, `Næste-knappen før ${nextStepLabel}`);
  await next.click();
  await waitForStep(nextStepLabel);
}

try {
  if (scenario === 'public-smoke') {
    await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
    traceStarted = true;
  }

  await runStep('01 public home', async () => {
    const response = await page.goto(baseUrl, {
      waitUntil: 'domcontentloaded',
      timeout: 45_000,
    });
    await expectOk(response, 'Production navigation');
    await page.locator('body').waitFor({ state: 'visible', timeout: 15_000 });
    report.initialUrl = page.url();
    report.title = await page.title();
  });

  if (scenario === 'full-case') {
    await runStep('02 dev login admin', async () => {
      const loginButton = page.getByRole('button', { name: 'Dev Login · Admin', exact: true });
      await loginButton.waitFor({ state: 'visible', timeout: 20_000 });
      await Promise.all([
        page.waitForURL((url) => url.pathname.startsWith('/app'), { timeout: 30_000 }),
        loginButton.click(),
      ]);
      await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => undefined);
      trackAuthenticatedApiFailures = true;
    }, { capture: false });

    await runStep('03 create draft case', async () => {
      const response = await page.goto(`${baseUrl}/app/job/new`, {
        waitUntil: 'domcontentloaded',
        timeout: 30_000,
      });
      await expectOk(response, 'Create-case navigation');
      await page.getByRole('heading', { name: 'Ny sag', exact: true })
        .waitFor({ state: 'visible', timeout: 20_000 });

      const destination = page.getByPlaceholder('Søg adresse...').first();
      await destination.fill(destinationAddress);
      await destination.press('Tab');
      await page.waitForTimeout(250);

      const customerPicker = page.getByRole('button', { name: 'Vælg kunde...', exact: true });
      await waitForEnabled(customerPicker, 'Kundevælgeren');
      await customerPicker.click();
      await page.getByRole('option', { name: /Opret ny kunde/ }).click();
      await page.getByPlaceholder('Kundenavn').fill(customerName);
      await page.getByPlaceholder('Email').fill(`playwright.${runStamp}@example.com`);
      await page.getByPlaceholder('Telefon').fill('20112233');
      await page.getByPlaceholder('Kontaktperson').fill('Playwright QA');

      const createResponsePromise = page.waitForResponse((candidate) =>
        candidate.request().method() === 'POST'
        && /\/api\/jobs(?:\?.*)?$/.test(candidate.url()),
      { timeout: 30_000 });

      const createButton = page.getByRole('button', { name: 'Opret sag', exact: true });
      await waitForEnabled(createButton, 'Opret sag-knappen');
      await createButton.click();

      const createResponse = await createResponsePromise;
      await expectOk(createResponse, 'Case creation');
      const created = await createResponse.json();
      if (!created?.id) throw new Error('Case creation response did not include an id.');
      report.jobId = created.id;
      report.reportNumber = created.reportNumber ?? null;

      await page.getByRole('heading', { name: 'Sagen er oprettet', exact: true })
        .waitFor({ state: 'visible', timeout: 30_000 });
    });

    await runStep('04 open case from list', async () => {
      await page.getByRole('button', { name: 'Til sagslisten', exact: true }).click();
      await page.waitForURL((url) => url.pathname === '/app', { timeout: 20_000 });
      const search = page.getByPlaceholder('Søg opgaver...');
      await search.waitFor({ state: 'visible', timeout: 20_000 });
      await search.fill(customerName);
      const card = page.locator('button.job-card').filter({ hasText: customerName }).first();
      await card.waitFor({ state: 'visible', timeout: 20_000 });
      await card.click();
      await page.waitForURL((url) => url.pathname === `/app/job/${report.jobId}`, { timeout: 20_000 });
      await page.getByRole('heading', { name: 'Rediger sag', exact: true })
        .waitFor({ state: 'visible', timeout: 20_000 });
      await waitForStep('Sagsdetaljer');
    });

    await runStep('05 choose installation and work type', async () => {
      await clickNext('Anlægstyper');
      const installationType = page.locator('button.choice-card.selection-card').first();
      await installationType.waitFor({ state: 'visible', timeout: 20_000 });
      await installationType.click();

      const workKind = page.locator('input[name="workKind"]').first();
      await workKind.waitFor({ state: 'visible', timeout: 20_000 });
      await workKind.check();

      const customWorkKind = page.getByPlaceholder('Skriv hvilken opgavetype der udføres');
      if (await customWorkKind.isVisible().catch(() => false)) {
        await customWorkKind.fill('Playwright testarbejde');
      }
    });

    await runStep('06 mark control points irrelevant', async () => {
      await clickNext('Kontrolpunkter');
      const irrelevantButtons = page.locator('button[title="Marker som ikke relevant"]');
      let safetyCounter = 0;
      while (await irrelevantButtons.count()) {
        if (safetyCounter++ > 50) throw new Error('Too many control-point categories to process safely.');
        await irrelevantButtons.first().click();
        await page.waitForTimeout(100);
      }
    });

    await runStep('07 add worksheet', async () => {
      await clickNext('Timesedler');
      await page.getByRole('button', { name: 'Tilføj timeseddel', exact: true }).click();

      const workerTrigger = page.locator('.worksheet-form .multi-select-trigger');
      if (await workerTrigger.count()) {
        const triggerText = (await workerTrigger.first().innerText()).trim();
        if (/Vælg montør/i.test(triggerText)) {
          await workerTrigger.first().click();
          const firstWorker = page.locator('.worksheet-form [role="option"]').first();
          await firstWorker.waitFor({ state: 'visible', timeout: 15_000 });
          await firstWorker.click();
        }
      }

      await page.getByLabel('Timer', { exact: true }).fill('1');
      await page.getByRole('button', { name: 'Tilføj', exact: true }).click();
      await page.locator('.worksheet-form').waitFor({ state: 'hidden', timeout: 30_000 });
      await waitForEnabled(page.getByRole('button', { name: 'Næste', exact: true }), 'Næste-knappen efter timeseddel');
    });

    await runStep('08 complete and submit case', async () => {
      await clickNext('Afslutning');
      const completed = page.getByRole('button', { name: 'Færdig', exact: true });
      await completed.waitFor({ state: 'visible', timeout: 20_000 });
      await completed.click();

      await clickNext('Attestering');
      const confirmation = page.getByRole('checkbox', { name: /Jeg bekræfter, at sagen er gennemgået/ });
      await confirmation.check();

      const submitResponsePromise = page.waitForResponse((candidate) =>
        candidate.request().method() === 'POST'
        && candidate.url().includes(`/api/jobs/${report.jobId}/status`),
      { timeout: 30_000 });

      await page.getByRole('button', { name: 'Attestér og indsend', exact: true }).click();
      await expectOk(await submitResponsePromise, 'Case submission');
      await page.getByRole('heading', { name: 'Sag sendt til kontoret', exact: true })
        .waitFor({ state: 'visible', timeout: 30_000 });
    });

    await runStep('09 approve submitted case', async () => {
      const response = await page.goto(`${baseUrl}/app/completed/${report.jobId}`, {
        waitUntil: 'domcontentloaded',
        timeout: 30_000,
      });
      await expectOk(response, 'Submitted-case navigation');
      await page.getByRole('heading', { name: 'Sagsoverblik', exact: true })
        .waitFor({ state: 'visible', timeout: 20_000 });

      const approve = page.locator('button:visible').filter({ hasText: /^Godkend$/ }).last();
      await approve.waitFor({ state: 'visible', timeout: 20_000 });
      await approve.click();

      const dialog = page.getByRole('dialog', { name: 'Godkend sag' });
      await dialog.waitFor({ state: 'visible', timeout: 15_000 });
      const approveResponsePromise = page.waitForResponse((candidate) =>
        candidate.request().method() === 'POST'
        && candidate.url().includes(`/api/jobs/${report.jobId}/status`),
      { timeout: 30_000 });
      await dialog.getByRole('button', { name: 'Godkend', exact: true }).click();
      await expectOk(await approveResponsePromise, 'Case approval');

      await page.goto(`${baseUrl}/app/completed/${report.jobId}`, {
        waitUntil: 'domcontentloaded',
        timeout: 30_000,
      });
      await page.getByRole('heading', { name: 'Sagsoverblik', exact: true })
        .waitFor({ state: 'visible', timeout: 20_000 });
      await page.locator('.job-number').filter({ hasText: 'Godkendt' }).first()
        .waitFor({ state: 'visible', timeout: 20_000 });
      report.finalStatus = 'Approved';
    });

    if (report.pageErrors.length > 0) {
      throw new Error(`Browser page errors were recorded: ${report.pageErrors.join(' | ')}`);
    }
    if (report.failedApiResponses.length > 0) {
      throw new Error(`Failed authenticated API calls were recorded: ${JSON.stringify(report.failedApiResponses)}`);
    }
  }
} catch (error) {
  failure = error;
  report.failure = error instanceof Error
    ? { message: redact(error.message), stack: redact(error.stack ?? '') }
    : { message: redact(String(error)) };
  try {
    await screenshot('failure');
  } catch {
    // The page may already be closed or unavailable.
  }
} finally {
  report.completedAt = new Date().toISOString();
  report.finalUrl = safeUrl(page.url());

  if (traceStarted) {
    try {
      await context.tracing.stop({ path: path.join(artifactDir, 'trace.zip') });
    } catch (error) {
      report.traceError = redact(error instanceof Error ? error.message : String(error));
    }
  }

  await writeFile(
    path.join(artifactDir, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  await browser.close();
}

if (failure) {
  throw failure;
}
