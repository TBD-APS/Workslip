import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { requireLoopbackOrigin } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270', 'WORKSLIP_PLAYWRIGHT_APP_URL');
const API_URL = requireLoopbackOrigin(process.env.WORKSLIP_PLAYWRIGHT_API_URL || 'http://127.0.0.1:5262', 'WORKSLIP_PLAYWRIGHT_API_URL');
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
  const { payload: user } = await api(actor, 'GET', '/api/auth/me', undefined, [200]);
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

async function ensureCrossTenantAuditor(superadmin, seededAuditor, targetOrganizationId) {
  if (seededAuditor.user.organizationId !== targetOrganizationId) return seededAuditor;

  // The common seed's Auditor and Admin share a tenant. Prepare a dedicated
  // control identity through DB-only onboarding, leaving those users intact.
  const name = 'Playwright WOR-814 control organization';
  const cvr = '90000814';
  const email = 'auditor-v1@workslip-playwright.invalid';
  const { payload: organizations } = await api(superadmin, 'GET', '/api/organizations', undefined, [200]);
  let organization = organizations.find((candidate) => candidate.cvr === cvr);
  if (organization) {
    assert.equal(organization.name, name, 'Auditor fixture CVR belongs to an unexpected organization.');
    const { payload: users } = await api(superadmin, 'GET', `/api/superadmin/users?search=${encodeURIComponent(email)}`, undefined, [200]);
    const existing = users.users.find((user) => user.email === email && user.organizationId === organization.id);
    assert.ok(existing && existing.userKind === 'InternalTest', 'Existing auditor fixture must be an internal test identity in the control organization.');
  } else {
    const { payload: created } = await api(superadmin, 'POST', '/api/organizations', {
      name,
      cvr,
      adminDisplayName: 'Playwright WOR-814 auditor',
      adminEmail: email,
      adminPhone: null,
    }, [200]);
    organization = created.organization;
    assert.ok(organization?.id && created.user?.id, 'Control organization onboarding returned no identity.');
    await api(superadmin, 'PATCH', `/api/superadmin/users/${created.user.id}`, {
      role: 'Auditor',
      userKind: 'InternalTest',
    }, [200]);
  }
  assert.notEqual(organization.id, targetOrganizationId, 'Auditor control fixture must be separate from the target organization.');
  const auditor = await identity(email, 'auditor');
  assert.equal(auditor.user.organizationId, organization.id, 'Auditor fixture resolved outside its control organization.');
  return auditor;
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
const seededAuditor = await identity(AUDITOR_EMAIL, 'auditor');
const admin = await identity(ADMIN_EMAIL, 'admin');
const auditor = await ensureCrossTenantAuditor(superadmin, seededAuditor, admin.user.organizationId);
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
  await desktop.page.locator('#auditor-workspace').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await desktop.page.locator(`#auditor-organization-${admin.user.organizationId}`).click();
  await desktop.page.locator(`#auditor-report-${report.id}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await saveScreenshot(desktop.page, 'auditor-v1-desktop-1280.png');

  await desktop.page.locator(`#auditor-report-${report.id}`).click();
  await desktop.page.locator('#auditor-report-detail').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await desktop.page.locator('#auditor-trace-heading').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  const description = 'Automatisk browser-evidens for WOR-814 auditor-flow.';
  const reference = 'Playwright WOR-814';
  const findingForm = desktop.page.locator('#auditor-finding-form');
  await findingForm.locator('#auditor-finding-description').fill(description);
  await findingForm.locator('#auditor-finding-reference').fill(reference);
  const createResponse = desktop.page.waitForResponse((response) =>
    response.request().method() === 'POST'
      && new URL(response.url()).pathname.endsWith(`/reports/${report.id}/findings`),
  { timeout: API_TIMEOUT });
  await findingForm.locator('#auditor-finding-submit').click();
  const findingResponse = await createResponse;
  assert.equal(findingResponse.status(), 200, `Creating auditor finding returned ${findingResponse.status()}.`);
  const finding = await findingResponse.json();
  assert.ok(finding.id, 'Creating auditor finding returned no id.');
  await desktop.page.locator(`#auditor-finding-${finding.id}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const { payload: persistedReport } = await api(auditor, 'GET', `/api/auditor/organizations/${admin.user.organizationId}/reports/${report.id}`, undefined, [200]);
  const persistedFinding = persistedReport.findings.find((candidate) => candidate.id === finding.id);
  assert.equal(persistedFinding?.description, description, 'Browser-created auditor finding was not persisted.');
  assert.equal(persistedFinding?.reference, reference, 'Browser-created auditor reference was not persisted.');
  await saveScreenshot(desktop.page, 'auditor-v1-detail-desktop-1280.png');
  assert.deepEqual(desktop.diagnostics, [], `Desktop auditor diagnostics: ${desktop.diagnostics.join(' | ')}`);
  await desktopContext.close();

  const mobileContext = await browser.newContext({ ...devices['iPhone 13'], locale: 'da-DK', timezoneId: 'Europe/Copenhagen' });
  const mobile = await authenticatePage(mobileContext, auditor, '/app/auditor');
  await mobile.page.locator('#auditor-workspace').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await mobile.page.locator(`#auditor-organization-${admin.user.organizationId}`).click();
  await mobile.page.locator('#auditor-report-cards').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await mobile.page.locator(`#auditor-report-card-${report.id}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await saveScreenshot(mobile.page, 'auditor-v1-mobile-390.png');
  assert.deepEqual(mobile.diagnostics, [], `Mobile auditor diagnostics: ${mobile.diagnostics.join(' | ')}`);
  await mobileContext.close();
} finally {
  await browser.close();
}

console.log('[playwright] Auditor v1 desktop/mobile evidence passed.');
