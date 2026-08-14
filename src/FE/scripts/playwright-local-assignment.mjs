import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { createContractHelpers } from './playwright-critical-contract.mjs';
import { createDomainHelpers } from './playwright-critical-domain.mjs';
import { createAdminScenarioHandlers } from './playwright-scenarios-admin.mjs';

const scriptPath = fileURLToPath(import.meta.url);
const VIEWPORT_NAME = 'iPhone 13';
const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;

export function validateLocalActionsEnvironment(env = process.env) {
  if (env.WORKSLIP_ALLOW_LOCAL_DEV_TOKEN !== 'true') {
    throw new Error('WORKSLIP_ALLOW_LOCAL_DEV_TOKEN must be exactly true for the isolated local Actions runner.');
  }

  const appUrl = validateLoopbackOrigin(env.WORKSLIP_LOCAL_APP_URL, 'WORKSLIP_LOCAL_APP_URL');
  const apiUrl = validateLoopbackOrigin(env.WORKSLIP_LOCAL_API_URL, 'WORKSLIP_LOCAL_API_URL');
  const userEmail = requireValue(env.WORKSLIP_SYNTHETIC_USER_EMAIL, 'WORKSLIP_SYNTHETIC_USER_EMAIL').toLowerCase();
  const adminEmail = requireValue(env.WORKSLIP_SYNTHETIC_ADMIN_EMAIL, 'WORKSLIP_SYNTHETIC_ADMIN_EMAIL').toLowerCase();

  return { appUrl, apiUrl, userEmail, adminEmail };
}

function validateLoopbackOrigin(value, name) {
  let url;
  try {
    url = new URL(value ?? '');
  } catch {
    throw new Error(`${name} must be a loopback HTTP origin without credentials, path, query, or fragment.`);
  }

  const loopback = new Set(['localhost', '127.0.0.1', '[::1]']);
  if (
    url.protocol !== 'http:'
    || !loopback.has(url.hostname)
    || url.username
    || url.password
    || url.search
    || url.hash
    || (url.pathname !== '/' && url.pathname !== '')
  ) {
    throw new Error(`${name} must be a loopback HTTP origin without credentials, path, query, or fragment.`);
  }

  return url.origin;
}

function requireValue(value, name) {
  const normalized = String(value ?? '').trim();
  if (!normalized) throw new Error(`${name} is required for the isolated local Actions runner.`);
  return normalized;
}

async function main() {
  const runtime = validateLocalActionsEnvironment();
  const frontendRoot = path.resolve(path.dirname(scriptPath), '..');
  const artifactDir = path.resolve(frontendRoot, '../../artifacts/playwright-local-assignment');
  const postmanPath = path.resolve(frontendRoot, '../BE/WorkslipApi/Postman/postman_collection.json');
  const runId = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

  await rm(artifactDir, { recursive: true, force: true });
  await mkdir(artifactDir, { recursive: true });

  const postman = JSON.parse(await readFile(postmanPath, 'utf8'));
  const { chromium, devices } = await import('playwright');
  const browser = await chromium.launch({ headless: true });
  const report = {
    scenario: 'assignment-lifecycle',
    target: 'isolated-local-actions',
    appUrl: runtime.appUrl,
    startedAt: new Date().toISOString(),
    browser: 'chromium',
    viewport: devices[VIEWPORT_NAME].viewport,
    dataPolicy: 'Ephemeral SQL Server + synthetic Development identities. No Entra, inbox, ACS, staging, production, or customer data.',
    scenarios: [],
    retainedFixtures: [],
    cleanupFailures: [],
  };

  const helperEnv = {
    APP_URL: runtime.appUrl,
    API_TIMEOUT,
    UI_TIMEOUT,
    VIEWPORT_NAME,
    ARTIFACT_DIR: artifactDir,
    postman,
    browser,
    devices,
    report,
  };
  const contractHelpers = createContractHelpers(helperEnv);
  const domainHelpers = createDomainHelpers(helperEnv, contractHelpers);
  const helpers = { ...contractHelpers, ...domainHelpers };
  const handlers = createAdminScenarioHandlers(
    { APP_URL: runtime.appUrl, API_TIMEOUT, UI_TIMEOUT, VIEWPORT_NAME, browser, devices, report },
    helpers,
  );
  const dataFactory = contractHelpers.buildDataFactory(postman, runId);
  const scenarioReport = {
    name: 'assignment-lifecycle',
    startedAt: new Date().toISOString(),
    status: 'running',
    steps: [],
    consoleErrors: [],
    pageErrors: [],
    failedRequests: [],
    failedApiResponses: [],
    contractChecks: [],
    generatedFixtures: [],
    coverageNotes: [],
  };
  report.scenarios.push(scenarioReport);

  let failure = null;
  const context = await browser.newContext({
    ...devices[VIEWPORT_NAME],
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  const page = await context.newPage();
  const auth = { token: null, user: null, role: null };
  const fixtures = { jobs: [], customers: [], users: [] };
  let captureAuthenticatedNetwork = false;

  page.on('pageerror', (error) => scenarioReport.pageErrors.push(contractHelpers.redact(error.message)));
  page.on('requestfailed', (request) => {
    const entry = {
      method: request.method(),
      url: contractHelpers.safeUrl(request.url()),
      error: contractHelpers.redact(request.failure()?.errorText ?? 'unknown'),
    };
    scenarioReport.failedRequests.push(entry);
    if (captureAuthenticatedNetwork && request.url().includes('/api/')) scenarioReport.failedApiResponses.push(entry);
  });
  page.on('response', (response) => {
    if (!captureAuthenticatedNetwork || !response.url().includes('/api/') || response.status() < 400) return;
    scenarioReport.failedApiResponses.push({
      method: response.request().method(),
      url: contractHelpers.safeUrl(response.url()),
      status: response.status(),
    });
  });

  const roleEmails = { User: runtime.userEmail, Admin: runtime.adminEmail };
  const session = {
    name: 'assignment-lifecycle',
    context,
    page,
    auth,
    fixtures,
    scenarioReport,
    data: dataFactory.forScenario('assignment-lifecycle'),
    step,
    login,
    logout,
    apiExpect,
    getReferenceData,
    getConfiguredUsers,
    getAddress,
    setAuthenticatedNetworkCapture(value) { captureAuthenticatedNetwork = value; },
  };

  try {
    await handlers['assignment-lifecycle'](session);
    contractHelpers.assertNoBrowserErrors(session);
    scenarioReport.status = 'passed';
  } catch (error) {
    failure = error;
    scenarioReport.status = 'failed';
    scenarioReport.failure = contractHelpers.serializeError(error);
  } finally {
    scenarioReport.completedAt = new Date().toISOString();
    report.completedAt = scenarioReport.completedAt;
    report.status = failure ? 'failed' : 'passed';
    if (failure) report.failure = contractHelpers.serializeError(failure);
    await writeFile(path.join(artifactDir, 'report.json'), JSON.stringify(report, null, 2));
    await context.close();
    await browser.close();
  }

  if (failure) throw failure;

  async function step(label, action) {
    const entry = { label, startedAt: new Date().toISOString(), status: 'running' };
    scenarioReport.steps.push(entry);
    try {
      const value = await action();
      entry.status = 'passed';
      entry.completedAt = new Date().toISOString();
      return value;
    } catch (error) {
      entry.status = 'failed';
      entry.completedAt = new Date().toISOString();
      entry.error = contractHelpers.serializeError(error);
      throw error;
    }
  }

  async function login(role = 'Admin') {
    const email = roleEmails[role];
    if (!email) throw new Error(`Unsupported local Actions role: ${role}.`);

    captureAuthenticatedNetwork = false;
    await page.goto(`${runtime.appUrl}/login`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    const browserTokenResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/dev/token',
    { timeout: API_TIMEOUT });
    await page.getByRole('button', { name: `Dev Login · ${role}`, exact: true }).click();
    const browserTokenResponse = await browserTokenResponsePromise;
    if (!browserTokenResponse.ok()) {
      throw new Error(`Development login UI for ${role} returned HTTP ${browserTokenResponse.status()}.`);
    }
    await page.waitForURL((url) => url.pathname.startsWith('/app'), { timeout: API_TIMEOUT });

    const directTokenResponse = await fetch(`${runtime.apiUrl}/api/dev/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ email }),
      signal: AbortSignal.timeout(API_TIMEOUT),
    });
    const tokenPayload = await directTokenResponse.json().catch(() => null);
    if (!directTokenResponse.ok || !tokenPayload?.token || !tokenPayload?.user) {
      throw new Error(`Development token contract for ${role} returned HTTP ${directTokenResponse.status}.`);
    }

    auth.token = tokenPayload.token;
    auth.user = tokenPayload.user;
    auth.role = tokenPayload.user.role;
    captureAuthenticatedNetwork = true;

    const me = await apiExpect('GET', '/api/auth/me', undefined, [200]);
    auth.user = me;
    auth.role = me.role;
    if (String(me.role).toLowerCase() !== role.toLowerCase()) {
      throw new Error(`Synthetic ${role} identity resolved to role ${me.role}.`);
    }
    return me;
  }

  async function logout() {
    const creationDialog = page.getByRole('dialog').filter({ has: page.getByRole('heading', { name: /sag(?:en|er) er oprettet/i }) });
    if (await creationDialog.isVisible().catch(() => false)) {
      await creationDialog.getByRole('button', { name: 'Til sagslisten', exact: true }).click();
      await creationDialog.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    }

    const button = page.getByRole('button', { name: 'Log ud', exact: true });
    await button.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await button.click();
    await page.waitForURL((url) => url.pathname === '/login', { timeout: UI_TIMEOUT });
    auth.token = null;
    auth.user = null;
    auth.role = null;
    captureAuthenticatedNetwork = false;
  }

  async function apiExpect(method, pathname, body, expectedStatuses = [200]) {
    const headers = { Accept: 'application/json' };
    if (auth.token) headers.Authorization = `Bearer ${auth.token}`;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(`${runtime.apiUrl}${pathname}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(API_TIMEOUT),
    });
    const contentType = response.headers.get('content-type') ?? '';
    const payload = contentType.includes('json')
      ? await response.json().catch(() => null)
      : await response.text().catch(() => null);
    if (!expectedStatuses.includes(response.status)) {
      throw new Error(`${method} ${pathname} returned HTTP ${response.status}; expected ${expectedStatuses.join('/')}. Body: ${contractHelpers.redact(JSON.stringify(payload))}`);
    }
    return payload;
  }

  async function getReferenceData() {
    const data = await apiExpect('GET', '/api/reference-data', undefined, [200]);
    if (!Array.isArray(data?.installationTypes) || !Array.isArray(data?.workKinds) || !Array.isArray(data?.closureFlags)) {
      throw new Error('Runtime reference data is incomplete.');
    }
    return data;
  }

  async function getConfiguredUsers(roles) {
    const users = contractHelpers.unwrapCollection(await apiExpect('GET', '/api/users/', undefined, [200]));
    return roles.map((role) => {
      const expectedEmail = roleEmails[role];
      const user = users.find((candidate) =>
        String(candidate?.email ?? '').trim().toLowerCase() === expectedEmail
        && String(candidate?.role ?? '').toLowerCase() === role.toLowerCase());
      if (!user?.id || !user.displayName) throw new Error(`Configured ${role} identity is not available in the synthetic organization.`);
      return user;
    });
  }

  async function getAddress() {
    const query = encodeURIComponent(session.data.addressQuery);
    const response = await fetch(`https://dawa.aws.dk/adresser/autocomplete?q=${query}&per_side=5`, {
      headers: { Accept: 'application/json' },
      signal: AbortSignal.timeout(API_TIMEOUT),
    });
    if (!response.ok) throw new Error(`DAWA autocomplete returned HTTP ${response.status}.`);
    const suggestions = await response.json();
    const suggestion = Array.isArray(suggestions) ? suggestions.find((item) => item?.tekst || item?.adresse?.betegnelse) : null;
    if (!suggestion) throw new Error('DAWA returned no address suggestion.');
    const text = suggestion.tekst ?? suggestion.adresse.betegnelse;
    const address = suggestion.adresse ?? suggestion.data ?? {};
    return {
      text,
      street: address.vejnavn && address.husnr ? `${address.vejnavn} ${address.husnr}` : text.split(',')[0],
      zipCode: address.postnr ?? address.postnummer?.nr ?? null,
      city: address.postnrnavn ?? address.postnummer?.navn ?? null,
      raw: suggestion,
    };
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === scriptPath) {
  await main();
}
