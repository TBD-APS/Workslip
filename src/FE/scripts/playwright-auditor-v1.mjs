import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

const APP_URL = new URL(process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270').origin;
const API_URL = new URL(process.env.WORKSLIP_PLAYWRIGHT_API_URL || 'http://127.0.0.1:5262').origin;
const EVIDENCE_DIR = process.env.WORKSLIP_PLAYWRIGHT_EVIDENCE_DIR?.trim() || '';
const AUDITOR_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_AUDITOR_EMAIL || '').trim();
const ADMIN_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL || '').trim();
const SUPERADMIN_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_SUPERADMIN_EMAIL || '').trim();
const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;

for (const [name, value] of Object.entries({ AUDITOR_EMAIL, ADMIN_EMAIL, SUPERADMIN_EMAIL })) {
  if (!value) throw new Error(`${name} is required for auditor browser evidence.`);
}

async function identity(email, expectedRole) {
  const tokenResponse = await fetch(`${API_URL}/api/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email }),
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  const tokenPayload = await tokenResponse.json().catch(() => null);
  assert.ok(tokenResponse.ok && tokenPayload?.token, `Could not issue ${expectedRole} token.`);

  const actor = { token: tokenPayload.token, email };
  const user = await api(actor, 'GET', '/api/auth/me', undefined, [200]);
  assert.equal(String(user.role).toLowerCase(), expectedRole.toLowerCase(), `${email} resolved to the wrong role.`);
  assert.ok(user.id && user.organizationId, `${expectedRole} identity is missing id/organizationId.`);
  return { ...actor, user };
}

async function api(actor, method, pathname, body, expectedStatuses) {
  const headers = { Accept: 'application/json', Authorization: `Bearer ${actor.token}` };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${API_URL}${pathname}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  const contentType = response.headers.get('content-type') ?? '';
  const payload = response.status === 204
    ? null
    : contentType.includes('json')
      ? await response.json().catch(() => null)
      : await response.text().catch(() => null);
  if (!expectedStatuses.includes(response.status)) {
    throw new Error(`${method} ${pathname} returned ${response.status}; expected ${expectedStatuses.join('/')}. Payload: ${JSON.stringify(payload)}`);
  }
  return { status: response.status, payload };
}

async function ensureAssignment(superadmin, auditor, targetOrganizationId) {
  const created = await api(superadmin, 'POST', '/api/auditor/admin/assignments', {
    auditorUserId: auditor.user.id,
    targetOrganizationId,
    authorizationArea: 'VVS',
    activeFrom: null,
    activeUntil: null,
  }, [200, 409]);

  if (created.status === 200) return created.payload;

  const assignments = await api(superadmin, 'GET', '/api/auditor/admin/assignments', undefined, [200]);
  const existing = assignments.payload.find((assignment) =>
    assignment.auditorUserId === auditor.user.id
      && assignment.targetOrganizationId === targetOrganizationId
      && assignment.isActive,
  );
  assert.ok(existing, 'Expected an existing active auditor assignment after conflict.');
  return existing;
}

async function saveScreenshot(page, filename) {
  if (!EVIDENCE_DIR) return;
  await mkdir(EVIDENCE_DIR, { recursive: true });
  await page.screenshot({ path: path.join(EVIDENCE_DIR, filename), fullPage: true });
}

async function authenticatePage(context, actor, pathName) {
  const page = await context.newPage();
  const diagnostics = [];
  page.on('pageerror', (error) => diagnostics.push(`page:${error.message}`));
  page.on('console', (message) => {
    if (message.type() === 'error') diagnostics.push(`console:${message.text()}`);
  });
  await page.goto(`${APP_URL}/login`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.evaluate(({ token, email }) => {
    localStorage.setItem('authToken', token);
    localStorage.setItem('userEmail', email);
  }, { token: actor.token, email: actor.email });
  await page.goto(`${APP_URL}${pathName}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.locator('#app-shell').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  return { page, diagnostics };
}

const superadmin = await identity(SUPERADMIN_EMAIL, 'superadmin');
const auditor = await identity(AUDITOR_EMAIL, 'auditor');
const admin = await identity(ADMIN_EMAIL, 'admin');
assert.notEqual(auditor.user.organizationId, admin.user.organizationId, 'Auditor evidence requires distinct control and target organizations.');

await ensureAssignment(superadmin, auditor, admin.user.organizationId);

const organizations = await api(auditor, 'GET', '/api/auditor/organizations', undefined, [200]);
const assignedOrganization = organizations.payload.find((organization) => organization.organizationId === admin.user.organizationId);
assert.ok(assignedOrganization, 'Auditor organizations endpoint did not expose the assigned target organization.');

const reports = await api(
  auditor,
  'GET',
  `/api/auditor/organizations/${admin.user.organizationId}/reports?limit=100&offset=0`,
  undefined,
  [200],
);
assert.ok(Array.isArray(reports.payload.items), 'Auditor report response is missing items.');
assert.ok(reports.payload.items.length > 0, 'Synthetic VVS seed must contain at least one approved auditor-scope Vand/Afløb report.');
const report = reports.payload.items[0];

const { chromium, devices } = await import('playwright');
const browser = await chromium.launch({ headless: true });
try {
  const desktopContext = await browser.newContext({ viewport: { width: 1280, height: 800 }, locale: 'da-DK', timezoneId: 'Europe/Copenhagen' });
  const desktop = await authenticatePage(desktopContext, auditor, '/app/auditor');
  await desktop.page.getByTestId('auditor-workspace').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await desktop.page.getByTestId(`auditor-organization-${admin.user.organizationId}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await desktop.page.getByTestId(`auditor-report-${report.id}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await saveScreenshot(desktop.page, 'auditor-v1-desktop-1280.png');

  await desktop.page.getByTestId(`auditor-report-${report.id}`).click();
  await desktop.page.getByTestId('auditor-report-detail').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await desktop.page.getByRole('heading', { name: 'Sporbarhed' }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  const findingForm = desktop.page.getByTestId('auditor-finding-form');
  await findingForm.getByLabel('Beskrivelse').fill('Automatisk browser-evidens for WOR-814 auditor-flow.');
  await findingForm.getByLabel('Reference til kontrolpunkt eller dokument').fill('Playwright WOR-814');
  const createResponse = desktop.page.waitForResponse((response) =>
    response.request().method() === 'POST'
      && new URL(response.url()).pathname.endsWith(`/reports/${report.id}/findings`),
  { timeout: API_TIMEOUT });
  await findingForm.getByRole('button', { name: 'Opret fund' }).click();
  const findingResponse = await createResponse;
  assert.equal(findingResponse.status(), 200, `Creating auditor finding returned ${findingResponse.status()}.`);
  await desktop.page.getByTestId('auditor-finding-list').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await saveScreenshot(desktop.page, 'auditor-v1-detail-desktop-1280.png');
  assert.deepEqual(desktop.diagnostics, [], `Desktop auditor diagnostics: ${desktop.diagnostics.join(' | ')}`);
  await desktopContext.close();

  const mobileContext = await browser.newContext({ ...devices['iPhone 13'], locale: 'da-DK', timezoneId: 'Europe/Copenhagen' });
  const mobile = await authenticatePage(mobileContext, auditor, '/app/auditor');
  await mobile.page.getByTestId('auditor-workspace').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await mobile.page.getByTestId('auditor-report-cards').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await saveScreenshot(mobile.page, 'auditor-v1-mobile-390.png');
  assert.deepEqual(mobile.diagnostics, [], `Mobile auditor diagnostics: ${mobile.diagnostics.join(' | ')}`);
  await mobileContext.close();
} finally {
  await browser.close();
}

console.log('[playwright] Auditor v1 desktop/mobile evidence passed.');
