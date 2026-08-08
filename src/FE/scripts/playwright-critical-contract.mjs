import process from 'node:process';

export function createContractHelpers(env) {
  const { API_TIMEOUT, UI_TIMEOUT, postman } = env;

function buildDataFactory(collection, runId) {
  const variables = Object.fromEntries((collection.variable ?? []).map((entry) => [entry.key, entry.value]));
  const customerTemplate = requiredPostmanBody(collection, '/api/customers (create)');
  const jobTemplate = requiredPostmanBody(collection, '/api/jobs');
  const userTemplate = requiredPostmanBody(collection, '/api/users');
  const organizationTemplate = requiredPostmanBody(collection, '/api/organizations');

  const defaultEmail = requiredSource(variables.testEmail, 'Postman variable testEmail');
  const emailDomain = defaultEmail.split('@')[1];
  if (!emailDomain) throw new Error('Postman variable testEmail must contain a domain.');
  const syntheticAdminEmail = requiredSource(
    process.env.WORKSLIP_SYNTHETIC_ADMIN_EMAIL,
    'WORKSLIP_SYNTHETIC_ADMIN_EMAIL',
  );
  const basePhone = requiredSource(variables.adminPhone || variables.userPhone || userTemplate.phone, 'Postman phone template');
  const baseOrg = requiredSource(variables.organizationName || organizationTemplate.name, 'Postman organizationName template');
  const baseAdmin = requiredSource(variables.adminDisplayName || organizationTemplate.adminDisplayName, 'Postman adminDisplayName template');
  const baseUser = requiredSource(variables.userDisplayName || userTemplate.displayName, 'Postman userDisplayName template');
  const customerBase = requiredSource(customerTemplate.name || jobTemplate.customerSnapshot?.name, 'Postman customer name template');
  const contactBase = requiredSource(customerTemplate.contactPerson || jobTemplate.customerSnapshot?.contactPerson, 'Postman contact-person template');
  const taskBase = requiredSource(jobTemplate.observations?.taskDescription, 'Postman task-description template');
  const addressSource = requiredSource(customerTemplate.address || jobTemplate.customerSnapshot?.address, 'Postman address template');
  const addressQuery = addressSearchSeed(addressSource);

  return {
    forScenario(name) {
      const suffix = `${runId}-${name}`.replace(/[^a-zA-Z0-9-]/g, '').slice(-40);
      const numeric = String(Date.now()).slice(-8);
      return {
        suffix,
        addressQuery,
        customerName: `${customerBase} PLAYWRIGHT ${suffix}`,
        updatedCustomerName: `${customerBase} PLAYWRIGHT UPDATED ${suffix}`,
        customerEmail: `playwright-customer+${suffix}@${emailDomain}`,
        contactPerson: `${contactBase} ${suffix}`,
        phone: String(basePhone),
        reportNumber: `PW-${suffix}`.slice(0, 50),
        taskDescription: `${taskBase} PLAYWRIGHT ${suffix}`,
        failedSaveText: `${taskBase} PLAYWRIGHT BLOCKED ${suffix}`,
        retriedSaveText: `${taskBase} PLAYWRIGHT RECOVERED ${suffix}`,
        correctedObservation: `${taskBase} PLAYWRIGHT CORRECTED ${suffix}`,
        customWorkKind: `${taskBase} PLAYWRIGHT CUSTOM ${suffix}`,
        inviteEmail: `playwright-invite+${suffix}@${emailDomain}`,
        inviteeDisplayName: `${baseUser} Invite ${suffix}`,
        userDisplayName: `${baseUser} ${suffix}`,
        userEmail: (index) => `playwright-user-${index}+${suffix}@${emailDomain}`,
        secondaryOrganization: {
          name: `${baseOrg} PLAYWRIGHT ${suffix}`,
          cvr: numeric,
          adminDisplayName: `${baseAdmin} ${suffix}`,
          adminEmail: plusAddress(syntheticAdminEmail, suffix),
          adminPhone: String(basePhone),
        },
      };
    },
  };
}

function plusAddress(email, tag) {
  const separator = email.lastIndexOf('@');
  if (separator <= 0 || separator === email.length - 1) {
    throw new Error('WORKSLIP_SYNTHETIC_ADMIN_EMAIL must contain a valid mailbox address.');
  }
  const local = email.slice(0, separator).split('+')[0];
  const domain = email.slice(separator + 1);
  const safeTag = String(tag).replace(/[^a-zA-Z0-9-]/g, '').slice(-40);
  return `${local}+${safeTag}@${domain}`;
}

function requiredSource(value, label) {
  if (value === undefined || value === null || String(value).trim() === '') {
    throw new Error(`${label} is required; the suite does not invent fallback data.`);
  }
  return String(value).trim();
}

function addressSearchSeed(value) {
  const source = requiredSource(value, 'Postman address template');
  const postalLocality = source.match(/\b\d{4}\s+[^,]+/u)?.[0];
  return postalLocality ?? source;
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
  if (!postmanMatch) throw new Error(`Postman collection does not define ${method} ${cleanPath}.`);
  return { method, path: cleanPath, openApiPath: openApiMatch[0], postmanRequest: postmanMatch.name };
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

function requiredPostmanBody(collection, requestName) {
  const body = postmanBody(collection, requestName);
  if (!body || Object.keys(body).length === 0) {
    throw new Error(`Postman request ${requestName} must contain a parseable JSON body.`);
  }
  return body;
}

function pickReferenceSelection(data) {
  const jobTemplate = requiredPostmanBody(postman, '/api/jobs');
  const expectedWorkKind = requiredSource(jobTemplate.work?.workKind, 'Postman job workKind template');
  const expectedClosureFlag = requiredSource(jobTemplate.work?.closureFlags?.[0], 'Postman job closureFlag template');
  const installations = [...data.installationTypes].sort(sortByOrder);
  const installation = installations.find((item) => item.id && item.name && item.categories?.some((category) => category.id));
  const workKind = [...data.workKinds].sort(sortByOrder)
    .find((item) => String(item.normalizedLabel).toLowerCase() === expectedWorkKind.toLowerCase());
  const closureFlag = [...data.closureFlags].sort(sortByOrder)
    .find((item) => String(item.normalizedLabel).toLowerCase() === expectedClosureFlag.toLowerCase());
  if (!installation || !workKind || !closureFlag) {
    throw new Error(`Runtime reference data does not satisfy Postman job template (${expectedWorkKind}, ${expectedClosureFlag}).`);
  }
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
async function clickByTextCandidates(locator, values, description) { for (const value of values) { const match = locator.filter({ hasText: value }).first(); if (await match.isVisible().catch(() => false)) { await match.click(); return; } } throw new Error(`No visible ${description} matched runtime values: ${values.join(', ')}.`); }
async function checkRadioByCandidates(page, values, description) { for (const value of values) { const radio = page.getByRole('radio', { name: value, exact: true }); if (await radio.isVisible().catch(() => false)) { await radio.check(); return; } const label = page.locator('label').filter({ hasText: value }).first(); if (await label.isVisible().catch(() => false)) { await label.click(); return; } } throw new Error(`No visible ${description} matched runtime values: ${values.join(', ')}.`); }
async function waitForApiResponse(page, method, pathname, statuses) { const response = await page.waitForResponse((candidate) => candidate.request().method() === method && new URL(candidate.url()).pathname.replace(/\/$/, '') === pathname.replace(/\/$/, ''), { timeout: API_TIMEOUT }); if (!statuses.includes(response.status())) throw new Error(`${method} ${pathname} returned HTTP ${response.status()}.`); return response; }
function assertNoBrowserErrors(session) { if (session.scenarioReport.pageErrors.length) throw new Error(`Unhandled page errors: ${session.scenarioReport.pageErrors.join(' | ')}`); const failedApi = session.scenarioReport.failedApiResponses.filter((item) => !item.expected); if (failedApi.length) throw new Error(`Unexpected failed API responses: ${JSON.stringify(failedApi)}`); }
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
