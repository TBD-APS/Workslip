import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { createContractHelpers } from './playwright-critical-contract.mjs';
import { createDomainHelpers } from './playwright-critical-domain.mjs';
import { requireLoopbackOrigin, seedLocalBrowserSession } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270',
  'WORKSLIP_PLAYWRIGHT_APP_URL',
);
const API_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_API_URL || 'http://127.0.0.1:5262',
  'WORKSLIP_PLAYWRIGHT_API_URL',
);
const ADMIN_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL || 'admin@17v3ygzs.mailosaur.net').trim();
const USER_EMAIL = String(process.env.WORKSLIP_PLAYWRIGHT_USER_EMAIL || 'user@17v3ygzs.mailosaur.net').trim();
const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;
const VIEWPORT = { width: 1280, height: 800 };
const TEST_ADDRESS = {
  text: 'Testvej 1, 8000 Aarhus C',
  street: 'Testvej 1',
  zipCode: '8000',
  city: 'Aarhus C',
};
const DAWA_RESPONSE = [{
  tekst: TEST_ADDRESS.text,
  adresse: {
    vejnavn: 'Testvej',
    husnr: '1',
    etage: null,
    dør: null,
    postnr: TEST_ADDRESS.zipCode,
    postnrnavn: TEST_ADDRESS.city,
  },
}];

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const postmanPath = path.resolve(scriptDirectory, '../../BE/WorkslipApi/Postman/postman_collection.json');
const postman = JSON.parse(await readFile(postmanPath, 'utf8'));
const contractHelpers = createContractHelpers({ API_TIMEOUT, UI_TIMEOUT, postman });
const domain = createDomainHelpers({ APP_URL, API_TIMEOUT, UI_TIMEOUT, postman }, contractHelpers);
const { chromium } = await import('playwright');
const browser = await chromium.launch({ headless: true });

try {
  console.log('[playwright] lifecycle: fixture create -> user submit -> admin approve.');
  await verifyKlsSubmitApproveLifecycle();

  console.log('[playwright] lifecycle: fixture create -> user submit -> reject -> correct -> resubmit -> approve.');
  await verifyRejectionCorrectionLifecycle();

  console.log('[playwright] critical job lifecycle flows passed.');
} finally {
  await browser.close();
}

async function createLifecycleSession({ email, role, suffix }) {
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    viewport: VIEWPORT,
  });
  await context.route('https://dawa.aws.dk/adresser/autocomplete**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(DAWA_RESPONSE),
    });
  });
  await context.route(
    (url) => !['127.0.0.1', 'localhost'].includes(url.hostname) && url.hostname !== 'dawa.aws.dk',
    (route) => route.fulfill({ status: 204, contentType: 'application/javascript', body: '' }),
  );

  const bootstrap = await seedLocalBrowserSession(context, {
    appUrl: APP_URL,
    apiUrl: API_URL,
    email,
  });
  assert.equal(String(bootstrap.user.role).toLowerCase(), role.toLowerCase(), `Synthetic ${role} identity resolved unexpectedly.`);

  const page = await context.newPage();
  const pageErrors = [];
  const consoleErrors = [];
  const failedApiRequests = [];
  const failedApiResponses = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('requestfailed', (request) => {
    const failure = request.failure()?.errorText ?? 'unknown';
    if (request.url().includes('/api/') && !/ERR_ABORTED/i.test(failure)) {
      failedApiRequests.push(`${request.method()} ${new URL(request.url()).pathname} ${failure}`);
    }
  });
  page.on('response', (response) => {
    if (response.url().includes('/api/') && response.status() >= 400) {
      failedApiResponses.push(`${response.request().method()} ${new URL(response.url()).pathname} ${response.status()}`);
    }
  });

  let idempotencySequence = 0;
  const apiExpect = async (method, pathname, body, expectedStatuses = [200]) => {
    const normalizedMethod = method.toUpperCase();
    const mutationHeaders = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(normalizedMethod)
      ? { 'Idempotency-Key': `playwright-lifecycle-${suffix}-${++idempotencySequence}` }
      : {};
    const response = await fetch(`${API_URL}${pathname}`, {
      method: normalizedMethod,
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${bootstrap.token}`,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...mutationHeaders,
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(API_TIMEOUT),
    });
    const contentType = response.headers.get('content-type') ?? '';
    const payload = contentType.includes('json')
      ? await response.json().catch(() => null)
      : await response.text().catch(() => null);
    if (!expectedStatuses.includes(response.status)) {
      throw new Error(`${normalizedMethod} ${pathname} returned HTTP ${response.status}; expected ${expectedStatuses.join('/')}.`);
    }
    return payload;
  };

  const user = await apiExpect('GET', '/api/auth/me', undefined, [200]);
  assert.equal(String(user.role).toLowerCase(), role.toLowerCase());

  const scenarioReport = {
    generatedFixtures: [],
    steps: [],
  };
  const unique = `${Date.now()}-${suffix}`;
  const session = {
    context,
    page,
    auth: { token: bootstrap.token, user },
    fixtures: { jobs: [], customers: [], users: [] },
    scenarioReport,
    address: TEST_ADDRESS,
    data: {
      customerName: `Playwright Lifecycle ${unique}`,
      customerEmail: `lifecycle-${unique}@example.test`,
      contactPerson: `Test Kontakt ${unique}`,
      phone: '20112233',
      taskDescription: `Lifecycle test ${unique}`,
      correctedObservation: `Rettet efter afvisning ${unique}`,
      customWorkKind: `Service ${unique}`,
    },
    apiExpect,
    async getReferenceData() {
      const data = await apiExpect('GET', '/api/reference-data', undefined, [200]);
      if (!Array.isArray(data?.installationTypes) || !Array.isArray(data?.workKinds) || !Array.isArray(data?.closureFlags)) {
        throw new Error('Runtime reference data is incomplete.');
      }
      return data;
    },
    async step(label, action) {
      scenarioReport.steps.push(label);
      return action();
    },
  };
  session.referenceData = await session.getReferenceData();

  return {
    session,
    async close() {
      await context.close();
    },
    assertCleanBrowser() {
      assert.deepEqual(pageErrors, [], `Lifecycle page errors: ${pageErrors.join(' | ')}`);
      assert.deepEqual(consoleErrors, [], `Lifecycle console errors: ${consoleErrors.join(' | ')}`);
      assert.deepEqual(failedApiRequests, [], `Lifecycle failed API requests: ${failedApiRequests.join(' | ')}`);
      assert.deepEqual(failedApiResponses, [], `Lifecycle failed API responses: ${failedApiResponses.join(' | ')}`);
    },
  };
}

async function resolveAssignedUser(adminSession, email) {
  const payload = await adminSession.apiExpect('GET', '/api/users/?limit=200', undefined, [200]);
  const users = contractHelpers.unwrapCollection(payload);
  const expected = email.trim().toLowerCase();
  const user = users.find((candidate) => String(candidate?.email ?? '').trim().toLowerCase() === expected);
  if (!user?.id || !user?.displayName) {
    throw new Error('Configured lifecycle User identity is not assignable in the active Development organization.');
  }
  return user;
}

async function createAssignedKlsJob(adminSession, assignedUser) {
  return adminSession.step('create assigned KLS fixture through API', async () => {
    const created = await adminSession.apiExpect('POST', '/api/jobs', {
      customerId: null,
      customerSnapshot: {
        name: adminSession.data.customerName,
        email: adminSession.data.customerEmail,
        phone: adminSession.data.phone,
        address: adminSession.address.text,
        contactPerson: adminSession.data.contactPerson,
      },
      createCustomerFromSnapshot: false,
      destinationAddress: adminSession.address.street,
      destinationZipCode: adminSession.address.zipCode,
      destinationCity: adminSession.address.city,
      jobType: 'KLS',
      assignedUserIds: [assignedUser.id],
      duplicatePerAssignedUser: false,
      linkedJobIds: [],
      work: null,
      observations: {
        reportDate: null,
        taskDescription: adminSession.data.taskDescription,
        customerObservations: null,
        technicalObservations: null,
      },
    }, [200, 201]);

    if (!created?.id) throw new Error('KLS fixture creation returned no id.');
    const createdJobIds = created.createdJobIds?.length ? created.createdJobIds : [created.id];
    adminSession.fixtures.jobs.push(...createdJobIds);
    for (const id of createdJobIds) {
      adminSession.scenarioReport.generatedFixtures.push({ type: 'job', id, source: 'runtime API fixture' });
    }

    const persisted = await adminSession.apiExpect('GET', `/api/jobs/${created.id}`, undefined, [200]);
    contractHelpers.assertStatus(persisted, ['Draft', 'Kladde']);
    assert.ok(
      (persisted.assignedUsers ?? []).some((candidate) => candidate.id === assignedUser.id),
      'KLS fixture must persist the configured assignee before the UI lifecycle starts.',
    );

    return {
      id: created.id,
      createdJobIds,
      reportNumber: created.reportNumber,
      customerName: adminSession.data.customerName,
      role: 'Admin',
    };
  });
}

async function verifyKlsSubmitApproveLifecycle() {
  const adminHarness = await createLifecycleSession({ email: ADMIN_EMAIL, role: 'Admin', suffix: 'approve-admin' });
  const userHarness = await createLifecycleSession({ email: USER_EMAIL, role: 'User', suffix: 'approve-user' });
  try {
    const assignedUser = await resolveAssignedUser(adminHarness.session, USER_EMAIL);
    const job = await createAssignedKlsJob(adminHarness.session, assignedUser);
    await domain.completeAndSubmitKlsViaUi(userHarness.session, job);

    const submitted = await userHarness.session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    contractHelpers.assertStatus(submitted, ['InReview']);
    assert.ok(
      (submitted.assignedUsers ?? []).some((candidate) => candidate.id === assignedUser.id),
      'Submitted lifecycle job must remain assigned to the executing User.',
    );

    await domain.approveJobViaUi(adminHarness.session, job.id);
    const approved = await adminHarness.session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    contractHelpers.assertStatus(approved, ['Approved', 'Godkendt']);

    await adminHarness.session.page.goto(`${APP_URL}/app/completed/${job.id}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await adminHarness.session.page.locator('#admin-case-information-title')
      .waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    adminHarness.assertCleanBrowser();
    userHarness.assertCleanBrowser();
  } finally {
    await adminHarness.close();
    await userHarness.close();
  }
}

async function verifyRejectionCorrectionLifecycle() {
  const adminHarness = await createLifecycleSession({ email: ADMIN_EMAIL, role: 'Admin', suffix: 'reject-admin' });
  const userHarness = await createLifecycleSession({ email: USER_EMAIL, role: 'User', suffix: 'reject-user' });
  const rejectionNote = 'Playwright: mangler dokumentation for udført arbejde.';

  try {
    const assignedUser = await resolveAssignedUser(adminHarness.session, USER_EMAIL);
    const job = await createAssignedKlsJob(adminHarness.session, assignedUser);
    await domain.completeAndSubmitKlsViaUi(userHarness.session, job);

    await domain.rejectJobViaUi(adminHarness.session, job.id, rejectionNote);
    const rejected = await adminHarness.session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    contractHelpers.assertStatus(rejected, ['Rejected', 'Afvist']);
    assert.equal(rejected.rejectionNote, rejectionNote, 'Rejection note must persist with the rejected job.');
    assert.ok(
      (rejected.assignedUsers ?? []).some((candidate) => candidate.id === assignedUser.id),
      'Rejected lifecycle job must be assigned back to the submitting User.',
    );

    await userHarness.session.page.goto(`${APP_URL}/app/job/${job.id}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await contractHelpers.waitForWizardStep(userHarness.session.page, 'Sagsdetaljer');
    const commentTrigger = userHarness.session.page.locator('#job-technical-observations-trigger');
    await commentTrigger.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    if ((await commentTrigger.getAttribute('aria-expanded')) !== 'true') {
      await commentTrigger.click();
    }
    const technical = userHarness.session.page.locator('#job-technical-observations');
    await technical.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await technical.fill(userHarness.session.data.correctedObservation);
    const correctionSave = contractHelpers.waitForApiResponse(userHarness.session.page, 'PATCH', `/api/jobs/${job.id}`, [200]);
    await domain.navigateToAttestation(userHarness.session, userHarness.session.referenceData);
    await correctionSave;

    await userHarness.session.page.locator('#job-attestation-confirmation').check();
    const resubmittedResponse = contractHelpers.waitForApiResponse(userHarness.session.page, 'POST', `/api/jobs/${job.id}/status`, [200]);
    await userHarness.session.page.locator('#job-attestation-submit').click();
    await resubmittedResponse;

    const resubmitted = await userHarness.session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    contractHelpers.assertStatus(resubmitted, ['InReview']);

    await domain.approveJobViaUi(adminHarness.session, job.id);
    const approved = await adminHarness.session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    contractHelpers.assertStatus(approved, ['Approved', 'Godkendt']);

    const history = await adminHarness.session.apiExpect('GET', `/api/jobs/${job.id}/history`, undefined, [200]);
    const historyText = JSON.stringify(history).toLowerCase();
    for (const expected of ['afvist', 'til gennemsyn', 'godkendt']) {
      assert.ok(historyText.includes(expected), `Job history must include status "${expected}".`);
    }

    adminHarness.assertCleanBrowser();
    userHarness.assertCleanBrowser();
  } finally {
    await adminHarness.close();
    await userHarness.close();
  }
}
