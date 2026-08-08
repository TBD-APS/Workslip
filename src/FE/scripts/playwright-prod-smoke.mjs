import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { chromium, devices } from 'playwright';
import { createContractHelpers } from './playwright-critical-contract.mjs';
import { createDomainHelpers } from './playwright-critical-domain.mjs';
import { createCoreScenarioHandlers } from './playwright-scenarios-core.mjs';
import { createAdminScenarioHandlers } from './playwright-scenarios-admin.mjs';
import { createSyntheticAuth } from './playwright-synthetic-auth.mjs';

const APP_URL = (process.env.PROD_URL ?? '').replace(/\/+$/, '');
const SCENARIO = process.env.SCENARIO ?? 'public-smoke';
const VIEWPORT_NAME = 'iPhone 13';
const ARTIFACT_DIR = path.resolve(process.cwd(), '../../artifacts/playwright-prod-smoke');
const POSTMAN_PATH = path.resolve(process.cwd(), '../BE/WorkslipApi/Postman/postman_collection.json');
const RUN_STARTED = new Date();
const RUN_ID = `${RUN_STARTED.getTime()}-${Math.random().toString(36).slice(2, 8)}`;
const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;
const INTERACTIVE_OTC_TIMEOUT = 5 * 60_000;

const CRITICAL_SCENARIOS = [
  'auth-session',
  'kls-lifecycle',
  'rejection-loop',
  'draft-recovery',
  'role-tenant-isolation',
  'invitation-onboarding',
  'assignment-lifecycle',
  'customer-lifecycle',
  'worksheet-integrity',
  'diverse-lifecycle',
];
const SUPPORTED_SCENARIOS = ['public-smoke', ...CRITICAL_SCENARIOS, 'all-critical'];

if (!APP_URL) throw new Error('PROD_URL is required.');
if (!SUPPORTED_SCENARIOS.includes(SCENARIO)) throw new Error(`Unsupported scenario: ${SCENARIO}`);

const syntheticAuth = createSyntheticAuth();
syntheticAuth.assertScenarioReady(SCENARIO);

await mkdir(ARTIFACT_DIR, { recursive: true });
const postman = JSON.parse(await readFile(POSTMAN_PATH, 'utf8'));

const report = {
  scenario: SCENARIO,
  appUrl: APP_URL,
  startedAt: RUN_STARTED.toISOString(),
  browser: 'chromium',
  viewport: devices[VIEWPORT_NAME].viewport,
  contractSources: {
    postmanCollection: path.relative(process.cwd(), POSTMAN_PATH),
    runtimeOpenApi: null,
  },
  dataPolicy: SCENARIO === 'public-smoke'
    ? 'Public smoke does not authenticate or send one-time codes.'
    : 'Authenticated flows use configured non-production identities and the normal Workslip one-time-code login. Codes are entered only in the visible browser. Generated test identifiers follow Postman collection templates.',
  scenarios: [],
  retainedFixtures: [],
  cleanupFailures: [],
};

const browser = await chromium.launch(syntheticAuth.browserLaunchOptions(SCENARIO));
const helperEnv = { APP_URL, API_TIMEOUT, UI_TIMEOUT, VIEWPORT_NAME, ARTIFACT_DIR, postman, browser, devices, report };
const contractHelpers = createContractHelpers(helperEnv);
const domainHelpers = createDomainHelpers(helperEnv, contractHelpers);
const helpers = { ...contractHelpers, ...domainHelpers };
const { buildPostmanContract, buildDataFactory, validateContract, serializeError, redact, safeUrl, fileSafe, assertNoBrowserErrors } = contractHelpers;
const postmanContract = buildPostmanContract(postman);
const dataFactory = SCENARIO === 'public-smoke'
  ? { forScenario: () => ({}) }
  : buildDataFactory(postman, RUN_ID);
const scenarioEnv = { APP_URL, API_TIMEOUT, UI_TIMEOUT, VIEWPORT_NAME, browser, devices, report };
const handlers = {
  ...createCoreScenarioHandlers(scenarioEnv, helpers),
  ...createAdminScenarioHandlers(scenarioEnv, helpers),
};
let suiteFailure = null;

try {
  const scenarios = SCENARIO === 'all-critical' ? CRITICAL_SCENARIOS : [SCENARIO];
  const scenarioFailures = [];
  for (const scenarioName of scenarios) {
    try {
      await runScenario(scenarioName);
    } catch (error) {
      scenarioFailures.push({ scenarioName, error });
      if (SCENARIO !== 'all-critical') throw error;
    }
  }
  if (scenarioFailures.length > 0) {
    throw new AggregateError(
      scenarioFailures.map((item) => item.error),
      `${scenarioFailures.length} critical Playwright scenario(s) failed: ${scenarioFailures.map((item) => item.scenarioName).join(', ')}`,
    );
  }
} catch (error) {
  suiteFailure = error;
} finally {
  report.completedAt = new Date().toISOString();
  report.status = suiteFailure ? 'failed' : 'passed';
  if (suiteFailure) report.failure = serializeError(suiteFailure);
  await writeFile(path.join(ARTIFACT_DIR, 'report.json'), JSON.stringify(report, null, 2));
  await browser.close();
}

if (suiteFailure) throw suiteFailure;

async function runScenario(name) {
  const scenarioReport = {
    name,
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

  const session = await createSession(name, scenarioReport);
  try {
    await handlers[name](session);
    assertNoBrowserErrors(session);
    scenarioReport.status = 'passed';
  } catch (error) {
    scenarioReport.status = 'failed';
    scenarioReport.failure = serializeError(error);
    try { await session.screenshot('failure'); } catch { /* best effort */ }
    throw error;
  } finally {
    scenarioReport.completedAt = new Date().toISOString();
    await session.cleanup();
    await session.context.close();
  }
}

async function authenticatePage(page, email) {
  const normalizedEmail = String(email ?? '').trim().toLowerCase();
  if (!normalizedEmail) throw new Error('Synthetic authentication requires an email address.');
  syntheticAuth.assertScenarioReady(SCENARIO);
  await page.goto(`${APP_URL}/login`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
  await page.getByRole('button', { name: 'Mistet dit login? Modtag engangskode', exact: true }).click();
  await page.getByLabel('Email', { exact: true }).fill(normalizedEmail);

  const sendResponsePromise = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/auth/send-code',
  { timeout: API_TIMEOUT });
  await page.getByRole('button', { name: 'Send kode', exact: true }).click();
  const sendResponse = await sendResponsePromise;
  if (!sendResponse.ok()) throw new Error(`OTC request returned HTTP ${sendResponse.status()}.`);

  await page.getByLabel('Engangskode', { exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  process.stdout.write('OTC sent. Enter the code in the visible Workslip browser and submit the login form.\n');
  const verifyResponse = await page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname.startsWith('/api/auth/verify-code/'),
  { timeout: INTERACTIVE_OTC_TIMEOUT });
  const tokenPayload = await verifyResponse.json().catch(() => null);
  if (!verifyResponse.ok() || !tokenPayload?.token || !tokenPayload?.user) {
    throw new Error(`OTC verification returned HTTP ${verifyResponse.status()}.`);
  }
  await page.waitForURL(
    (url) => url.pathname.startsWith('/app') || url.pathname.startsWith('/superadmin'),
    { timeout: API_TIMEOUT },
  );

  return { tokenPayload, apiBase: new URL(verifyResponse.url()).origin };
}

async function createSession(name, scenarioReport) {
  const context = await browser.newContext({
    ...devices[VIEWPORT_NAME],
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  const page = await context.newPage();
  const fixtures = { jobs: [], customers: [], users: [] };
  const auth = { token: null, user: null, apiBase: null, openApi: null, role: null };
  let captureAuthenticatedNetwork = false;

  page.on('console', (message) => {
    if (message.type() === 'error') scenarioReport.consoleErrors.push(redact(message.text()));
  });
  page.on('pageerror', (error) => scenarioReport.pageErrors.push(redact(error.message)));
  page.on('requestfailed', (request) => {
    const entry = { method: request.method(), url: safeUrl(request.url()), error: redact(request.failure()?.errorText ?? 'unknown') };
    scenarioReport.failedRequests.push(entry);
    if (captureAuthenticatedNetwork && request.url().includes('/api/')) scenarioReport.failedApiResponses.push(entry);
  });
  page.on('response', (response) => {
    if (!captureAuthenticatedNetwork || !response.url().includes('/api/') || response.status() < 400) return;
    scenarioReport.failedApiResponses.push({ method: response.request().method(), url: safeUrl(response.url()), status: response.status() });
  });

  const session = {
    name,
    context,
    page,
    auth,
    fixtures,
    scenarioReport,
    data: dataFactory.forScenario(name),
    step,
    screenshot,
    login,
    authenticateEmail,
    logout,
    api,
    apiExpect,
    getReferenceData,
    getAddress,
    cleanup,
    setAuthenticatedNetworkCapture(value) { captureAuthenticatedNetwork = value; },
  };
  return session;

  async function step(label, action, { screenshot: capture = true } = {}) {
    const entry = { label, startedAt: new Date().toISOString(), status: 'running' };
    scenarioReport.steps.push(entry);
    try {
      const value = await action();
      entry.status = 'passed';
      entry.completedAt = new Date().toISOString();
      if (capture) await screenshot(label);
      return value;
    } catch (error) {
      entry.status = 'failed';
      entry.completedAt = new Date().toISOString();
      entry.error = serializeError(error);
      try { await screenshot(`${label}-failed`); } catch { /* preserve original error */ }
      throw error;
    }
  }

  async function screenshot(label) {
    await page.screenshot({
      path: path.join(ARTIFACT_DIR, `${fileSafe(name)}-${fileSafe(label)}.png`),
      fullPage: true,
      mask: [
        page.getByLabel('Email', { exact: true }),
        page.getByLabel('Engangskode', { exact: true }),
      ],
    });
  }

  async function login(role = 'Admin') {
    const email = syntheticAuth.emailForRole(role);
    const me = await authenticateEmail(email);
    if (String(me.role).toLowerCase() !== String(role).toLowerCase()) {
      throw new Error(`Synthetic ${role} identity resolved to role ${me.role}.`);
    }
    return me;
  }

  async function authenticateEmail(email) {
    const { tokenPayload, apiBase } = await authenticatePage(page, email);
    auth.token = tokenPayload.token;
    auth.user = tokenPayload.user;
    auth.role = tokenPayload.user.role;
    auth.apiBase = apiBase;
    auth.openApi = null;
    captureAuthenticatedNetwork = true;
    await loadRuntimeContracts();
    const me = await apiExpect('GET', '/api/auth/me', undefined, [200]);
    auth.user = me;
    auth.role = me.role;
    return me;
  }

  async function logout() {
    const button = page.getByRole('button', { name: 'Log ud', exact: true });
    await button.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await button.click();
    await page.waitForURL((url) => url.pathname === '/login', { timeout: UI_TIMEOUT });
    auth.token = null;
    auth.user = null;
    auth.role = null;
    captureAuthenticatedNetwork = false;
  }

  async function loadRuntimeContracts() {
    if (auth.openApi) return;
    const response = await fetch(`${auth.apiBase}/openapi/v1.json`, { signal: AbortSignal.timeout(API_TIMEOUT) });
    if (!response.ok) throw new Error(`Runtime OpenAPI returned HTTP ${response.status}.`);
    auth.openApi = await response.json();
    report.contractSources.runtimeOpenApi ??= `${auth.apiBase}/openapi/v1.json`;
  }

  async function api(method, pathname, body, options = {}) {
    if (!auth.apiBase) throw new Error('API base is unavailable before login.');
    await loadRuntimeContracts();
    const methodUpper = method.toUpperCase();
    const contract = validateContract(methodUpper, pathname, auth.openApi, postmanContract);
    scenarioReport.contractChecks.push(contract);
    const token = Object.hasOwn(options, 'token') ? options.token : auth.token;
    const headers = { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...options.headers };
    if (token) headers.Authorization = `Bearer ${token}`;
    const response = await fetch(`${auth.apiBase}${pathname}`, {
      method: methodUpper,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(options.timeout ?? API_TIMEOUT),
    });
    let payload = null;
    const contentType = response.headers.get('content-type') ?? '';
    if (contentType.includes('json')) payload = await response.json().catch(() => null);
    else payload = await response.text().catch(() => null);
    return { response, payload };
  }

  async function apiExpect(method, pathname, body, expectedStatuses = [200], options = {}) {
    const result = await api(method, pathname, body, options);
    if (!expectedStatuses.includes(result.response.status)) {
      throw new Error(`${method} ${pathname} returned HTTP ${result.response.status}; expected ${expectedStatuses.join('/')}. Body: ${redact(JSON.stringify(result.payload))}`);
    }
    return result.payload;
  }

  async function getReferenceData() {
    const data = await apiExpect('GET', '/api/reference-data', undefined, [200]);
    if (!Array.isArray(data?.installationTypes) || !Array.isArray(data?.workKinds) || !Array.isArray(data?.closureFlags)) {
      throw new Error('Runtime reference data is missing installationTypes, workKinds, or closureFlags.');
    }
    return data;
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

  async function cleanup() {
    if (fixtures.jobs.length === 0 && fixtures.customers.length === 0 && fixtures.users.length === 0) return;
    if (!auth.apiBase) {
      report.cleanupFailures.push({ scenario: name, fixture: 'all', error: { message: 'API base unavailable for cleanup.' } });
      return;
    }

    let cleanupContext = null;
    try {
      cleanupContext = await browser.newContext({ ...devices[VIEWPORT_NAME], locale: 'da-DK' });
      const cleanupPage = await cleanupContext.newPage();
      const adminEmail = syntheticAuth.emailForRole('Admin');
      const { tokenPayload } = await authenticatePage(cleanupPage, adminEmail);
      const cleanupToken = tokenPayload.token;

      for (const jobId of [...fixtures.jobs].reverse()) {
        try {
          const job = await apiExpect('GET', `/api/jobs/${jobId}`, undefined, [200, 404], { token: cleanupToken });
          if (job?.worksheets) {
            for (const worksheet of [...job.worksheets].reverse()) {
              await apiExpect('DELETE', `/api/worksheets/${worksheet.id}/jobs/${jobId}`, undefined, [200, 204, 404], { token: cleanupToken });
            }
          }
          await apiExpect('DELETE', `/api/jobs/${jobId}`, undefined, [200, 204, 404], { token: cleanupToken });
        } catch (error) {
          report.cleanupFailures.push({ scenario: name, fixture: `job:${jobId}`, error: serializeError(error) });
        }
      }
      for (const customerId of [...fixtures.customers].reverse()) {
        try { await apiExpect('DELETE', `/api/customers/${customerId}`, undefined, [200, 204, 404], { token: cleanupToken }); }
        catch (error) { report.cleanupFailures.push({ scenario: name, fixture: `customer:${customerId}`, error: serializeError(error) }); }
      }
      for (const userId of [...fixtures.users].reverse()) {
        try { await apiExpect('DELETE', `/api/users/${userId}`, undefined, [200, 204, 404], { token: cleanupToken }); }
        catch (error) { report.cleanupFailures.push({ scenario: name, fixture: `user:${userId}`, error: serializeError(error) }); }
      }
    } catch (error) {
      report.cleanupFailures.push({ scenario: name, fixture: 'cleanup-session', error: serializeError(error) });
    } finally {
      await cleanupContext?.close();
    }
  }
}
