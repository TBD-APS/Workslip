export function createContractHelpers(env) {
  const { API_TIMEOUT, UI_TIMEOUT, postman } = env;

function buildDataFactory(collection, runId) {
  const variables = Object.fromEntries((collection.variable ?? []).map((entry) => [entry.key, entry.value]));
  const defaultEmail = String(variables.testEmail ?? 'integration@invalid.example');
  const emailDomain = defaultEmail.includes('@') ? defaultEmail.split('@')[1] : 'invalid.example';
  const basePhone = variables.adminPhone || variables.userPhone || '+4512345678';
  const baseOrg = variables.organizationName || 'Integration Organization';
  const baseAdmin = variables.adminDisplayName || 'Integration Admin';
  const baseUser = variables.userDisplayName || 'Integration User';
  const customerTemplate = postmanBody(collection, '/api/customers (create)');
  const jobTemplate = postmanBody(collection, '/api/jobs');
  const customerBase = customerTemplate.name || jobTemplate.customerSnapshot?.name || 'Integration Customer';
  const contactBase = customerTemplate.contactPerson || jobTemplate.customerSnapshot?.contactPerson || 'Integration Contact';
  return {
    forScenario(name) {
      const suffix = `${runId}-${name}`.replace(/[^a-zA-Z0-9-]/g, '').slice(-40);
      const numeric = String(Date.now()).slice(-8);
      return {
        suffix,
        addressQuery: 'Aarhus',
        customerName: `${customerBase} PLAYWRIGHT ${suffix}`,
        updatedCustomerName: `${customerBase} PLAYWRIGHT UPDATED ${suffix}`,
        customerEmail: `playwright-customer+${suffix}@${emailDomain}`,
        contactPerson: `${contactBase} ${suffix}`,
        phone: String(basePhone),
        reportNumber: `PW-${suffix}`.slice(0, 50),
        taskDescription: `Playwright task ${suffix}`,
        failedSaveText: `Playwright blocked save ${suffix}`,
        retriedSaveText: `Playwright recovered save ${suffix}`,
        correctedObservation: `Playwright correction ${suffix}`,
        rejectionReason: `Playwright rejection ${suffix}`,
        customWorkKind: `Playwright custom work ${suffix}`,
        inviteEmail: `playwright-invite+${suffix}@${emailDomain}`,
        inviteeDisplayName: `${baseUser} Invite ${suffix}`,
        userDisplayName: `${baseUser} ${suffix}`,
        userEmail: (index) => `playwright-user-${index}+${suffix}@${emailDomain}`,
        secondaryOrganization: {
          name: `${baseOrg} PLAYWRIGHT ${suffix}`,
          cvr: numeric,
          adminDisplayName: `${baseAdmin} ${suffix}`,
          adminEmail: `playwright-admin+${suffix}@${emailDomain}`,
          adminPhone: String(basePhone),
        },
      };
    },
  };
}

function buildPostmanContract(collection) {
  const requests = [];
  const walk = (items) => {
    for (const item of items ?? []) {
      if (item.item) walk(item.item);
      if (!item.request) continue;
      const raw = typeof item.request.url === 'string' ? item.request.url : item.request.url?.raw;
      if (!raw) continue;
      const pathname = raw.replace(/^\{\{baseUrl\}\}/, '').split('?')[0] || '/';
      requests.push({ name: item.name, method: item.request.method.toUpperCase(), template: pathname });
    }
  };
  walk(collection.item);
  return requests;
}

function validateContract(method, pathname, openApi, postmanRequests) {
  const cleanPath = pathname.split('?')[0];
  const openApiMatch = Object.entries(openApi.paths ?? {}).find(([template, methods]) =>
    method in Object.fromEntries(Object.keys(methods).map((key) => [key.toUpperCase(), methods[key]])) && pathMatches(template, cleanPath));
  if (!openApiMatch) throw new Error(`Runtime OpenAPI does not define ${method} ${cleanPath}.`);
  const postmanMatch = postmanRequests.find((request) => request.method === method && pathMatches(request.template, cleanPath));
  return { method, path: cleanPath, openApiPath: openApiMatch[0], postmanRequest: postmanMatch?.name ?? null };
}

function pathMatches(template, pathname) {
  const normalizedTemplate = template.replace(/\/$/, '') || '/';
  const normalizedPath = pathname.replace(/\/$/, '') || '/';
  const escaped = escapeRegex(normalizedTemplate)
    .replace(/\\\{\\\{[^}]+\\\}\\\}/g, '[^/]+')
    .replace(/\\\{[^}]+\\\}/g, '[^/]+');
  const regex = new RegExp(`^${escaped}$`);
  return regex.test(normalizedPath);
}

function postmanBody(collection, requestName) {
  let found = null;
  const walk = (items) => {
    for (const item of items ?? []) {
      if (item.item) walk(item.item);
      if (!found && item.name === requestName && item.request?.body?.raw) found = item.request.body.raw;
    }
  };
  walk(collection.item);
  if (!found) return {};
  try { return JSON.parse(found.replace(/\{\{[^}]+\}\}/g, '')); }
  catch { return {}; }
}

function pickReferenceSelection(data) {
  const installations = [...data.installationTypes].sort(sortByOrder);
  const installation = installations.find((item) => item.id && item.name && item.categories?.some((category) => category.id)) ?? installations[0];
  const workKinds = [...data.workKinds].sort(sortByOrder);
  const workKind = workKinds.find((item) => !item.requiresCustomWorkKind) ?? workKinds[0];
  const closureFlags = [...data.closureFlags].sort(sortByOrder);
  const closureFlag = closureFlags[0];
  if (!installation || !workKind || !closureFlag) throw new Error('Reference data did not contain a usable installation, work kind, and closure flag.');
  return { installation, workKind, closureFlag };
}

function sortByOrder(left, right) { return Number(left.sortOrder ?? 0) - Number(right.sortOrder ?? 0); }
function valueOf(item) { return item.value ?? item.normalizedLabel ?? item.name ?? item.label; }
function candidates(item) { return [...new Set([item?.label, item?.name, item?.normalizedLabel, item?.value].filter(Boolean).map(String))]; }
function assignedIds(job) { return (job.assignedUsers ?? []).map((item) => item.id); }
function readCustomerName(job) { return job.customer?.name ?? job.customerSnapshot?.name ?? job.customerName ?? null; }
function readDestinationAddress(job) { return job.destinationAddress ?? job.customer?.address ?? job.customerSnapshot?.address ?? null; }
function assertStatus(job, expected) { const actual = String(job.status ?? ''); if (!expected.some((item) => item.toLowerCase() === actual.toLowerCase())) throw new Error(`Expected status ${expected.join('/')} but got ${actual}.`); }
function assertEqual(actual, expected, label) { if (actual !== expected) throw new Error(`${label}: expected ${expected}; got ${actual}.`); }
function unwrapCollection(payload) { if (Array.isArray(payload)) return payload; for (const key of ['items', 'users', 'jobs', 'customers', 'results']) if (Array.isArray(payload?.[key])) return payload[key]; return []; }
function extractInviteToken(value) { if (!value) return null; const text = String(value); return text.includes('/') ? text.split('/').filter(Boolean).pop() : text; }
function sectionByHeading(page, heading) { return page.locator('section').filter({ has: page.getByRole('heading', { name: heading, exact: true }) }).first(); }

async function fillIfVisible(locator, value) { if (await locator.isVisible().catch(() => false)) await locator.fill(String(value ?? '')); }
async function waitForEnabled(locator, description, timeout = UI_TIMEOUT) { await locator.waitFor({ state: 'visible', timeout }); const start = Date.now(); while (await locator.isDisabled()) { if (Date.now() - start > timeout) throw new Error(`${description} remained disabled.`); await new Promise((resolve) => setTimeout(resolve, 150)); } }
async function waitForWizardStep(page, label) { await page.getByRole('button', { name: `${label} - aktuelt trin`, exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT }); }
async function currentWizardStep(page) { for (const label of ['Sagsdetaljer', 'Anlægstyper', 'Kontrolpunkter', 'Timesedler', 'Afslutning', 'Attestering']) if (await page.getByRole('button', { name: `${label} - aktuelt trin`, exact: true }).isVisible().catch(() => false)) return label; return null; }
async function clickNext(page, nextStep) { const button = page.getByRole('button', { name: 'Næste', exact: true }); await waitForEnabled(button, `Næste before ${nextStep}`); await button.click(); await waitForWizardStep(page, nextStep); }
async function clickWizardStep(page, label) { const button = page.getByRole('button', { name: new RegExp(`^${escapeRegex(label)}`) }); await button.click(); await waitForWizardStep(page, label); }
async function clickByTextCandidates(locator, values, description) { for (const value of values) { const match = locator.filter({ hasText: new RegExp(`^\\s*${escapeRegex(value)}\\s*$`, 'i') }).first(); if (await match.isVisible().catch(() => false)) { await match.click(); return; } } throw new Error(`No visible ${description} matched runtime values: ${values.join(', ')}.`); }
async function checkRadioByCandidates(page, values, description) { for (const value of values) { const radio = page.getByRole('radio', { name: value, exact: true }); if (await radio.isVisible().catch(() => false)) { await radio.check(); return; } const label = page.locator('label').filter({ hasText: new RegExp(`^\\s*${escapeRegex(value)}\\s*$`, 'i') }).first(); if (await label.isVisible().catch(() => false)) { await label.click(); return; } } throw new Error(`No visible ${description} matched runtime values: ${values.join(', ')}.`); }
async function waitForApiResponse(page, method, pathname, statuses) { const response = await page.waitForResponse((candidate) => candidate.request().method() === method && new URL(candidate.url()).pathname.replace(/\/$/, '') === pathname.replace(/\/$/, ''), { timeout: API_TIMEOUT }); if (!statuses.includes(response.status())) throw new Error(`${method} ${pathname} returned HTTP ${response.status()}.`); return response; }
function assertNoBrowserErrors(session) { if (session.scenarioReport.pageErrors.length) throw new Error(`Unhandled page errors: ${session.scenarioReport.pageErrors.join(' | ')}`); const failedApi = session.scenarioReport.failedApiResponses.filter((item) => !item.expected && ![401, 403, 404].includes(item.status)); if (failedApi.length) throw new Error(`Unexpected failed API responses: ${JSON.stringify(failedApi)}`); }
function serializeError(error) { return { message: redact(error instanceof Error ? error.message : String(error)), stack: redact(error instanceof Error ? error.stack ?? '' : '') }; }
function redact(value) { return String(value ?? '').replace(/Bearer\s+[^\s,;]+/gi, 'Bearer [REDACTED]').replace(/\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b/g, '[REDACTED_TOKEN]').replace(/[?&](code|token|state|session_state)=[^&\s]+/gi, '$1=[REDACTED]'); }
function safeUrl(value) { try { const url = new URL(value); for (const key of [...url.searchParams.keys()]) url.searchParams.set(key, '[REDACTED]'); url.hash = ''; return url.toString(); } catch { return redact(value); } }
function fileSafe(value) { return String(value).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 100); }
function escapeRegex(value) { return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

  return {
    buildDataFactory,
    buildPostmanContract,
    validateContract,
    pathMatches,
    postmanBody,
    pickReferenceSelection,
    sortByOrder,
    valueOf,
    candidates,
    assignedIds,
    readCustomerName,
    readDestinationAddress,
    assertStatus,
    assertEqual,
    unwrapCollection,
    extractInviteToken,
    sectionByHeading,
    fillIfVisible,
    waitForEnabled,
    waitForWizardStep,
    currentWizardStep,
    clickNext,
    clickWizardStep,
    clickByTextCandidates,
    checkRadioByCandidates,
    waitForApiResponse,
    assertNoBrowserErrors,
    serializeError,
    redact,
    safeUrl,
    fileSafe,
    escapeRegex
  };
}
