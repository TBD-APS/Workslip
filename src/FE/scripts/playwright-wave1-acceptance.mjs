import process from 'node:process';

const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;
const MOBILE_DEVICE_NAME = 'iPhone 13';

function requireRuntime() {
  if (process.env.WORKSLIP_ALLOW_LOCAL_DEV_TOKEN !== 'true') {
    throw new Error('WORKSLIP_ALLOW_LOCAL_DEV_TOKEN must be exactly true for Wave 1 acceptance checks.');
  }

  const appUrl = validateLoopbackOrigin(process.env.WORKSLIP_LOCAL_APP_URL, 'WORKSLIP_LOCAL_APP_URL');
  const apiUrl = validateLoopbackOrigin(process.env.WORKSLIP_LOCAL_API_URL, 'WORKSLIP_LOCAL_API_URL');
  const adminEmail = requireValue(process.env.WORKSLIP_SYNTHETIC_ADMIN_EMAIL, 'WORKSLIP_SYNTHETIC_ADMIN_EMAIL');
  const userEmail = requireValue(process.env.WORKSLIP_SYNTHETIC_USER_EMAIL, 'WORKSLIP_SYNTHETIC_USER_EMAIL');
  return { appUrl, apiUrl, adminEmail, userEmail };
}

function validateLoopbackOrigin(value, name) {
  let url;
  try {
    url = new URL(value ?? '');
  } catch {
    throw new Error(`${name} must be a loopback HTTP origin.`);
  }
  if (url.protocol !== 'http:' || !new Set(['localhost', '127.0.0.1', '[::1]']).has(url.hostname)) {
    throw new Error(`${name} must be a loopback HTTP origin.`);
  }
  return url.origin;
}

function requireValue(value, name) {
  const normalized = String(value ?? '').trim();
  if (!normalized) throw new Error(`${name} is required.`);
  return normalized;
}

function todayInCopenhagen() {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Copenhagen',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
}

function currentMonthInCopenhagen() {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'Europe/Copenhagen',
    year: 'numeric',
    month: 'numeric',
  }).formatToParts(new Date());
  return {
    year: Number(parts.find((part) => part.type === 'year')?.value),
    month: Number(parts.find((part) => part.type === 'month')?.value),
  };
}

function readCustomerName(job) {
  return job?.customer?.name ?? job?.customerSnapshot?.name ?? job?.customerName ?? null;
}

function normalizePathname(url) {
  return new URL(url).pathname.replace(/\/$/, '');
}

async function getDevIdentity(runtime, role) {
  const email = role === 'Admin' ? runtime.adminEmail : runtime.userEmail;
  const response = await fetch(`${runtime.apiUrl}/api/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email }),
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok || !payload?.token || !payload?.user?.email) {
    throw new Error(`Could not issue synthetic ${role} token (HTTP ${response.status}).`);
  }

  const identity = { token: payload.token, user: payload.user, email };
  const me = await apiRequest(runtime, identity, 'GET', '/api/auth/me', undefined, [200]);
  if (!me?.id || String(me.role ?? '').toLowerCase() !== role.toLowerCase()) {
    throw new Error(`Could not resolve synthetic ${role} identity through /api/auth/me.`);
  }

  return { ...identity, user: me };
}

async function apiRequest(runtime, identity, method, pathname, body, expectedStatuses, extraHeaders = {}) {
  const headers = {
    Accept: 'application/json',
    Authorization: `Bearer ${identity.token}`,
    ...extraHeaders,
  };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${runtime.apiUrl}${pathname}`, {
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
    throw new Error(`${method} ${pathname} returned ${response.status}; expected ${expectedStatuses.join('/')}.`);
  }
  return payload;
}

async function createBrowser(viewportName) {
  const { chromium, devices } = await import('playwright');
  const browser = await chromium.launch({ headless: true });
  const contextOptions = viewportName === 'desktop'
    ? { viewport: { width: 1280, height: 800 } }
    : devices[MOBILE_DEVICE_NAME];
  const context = await browser.newContext({
    ...contextOptions,
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  const page = await context.newPage();
  return { browser, context, page };
}

function captureBrowserFailures(page) {
  const pageErrors = [];
  const consoleErrors = [];
  const failedRequests = [];
  const failedApiResponses = [];
  const expectedApiFailures = new Set();
  let allowExpected404Console = false;

  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() !== 'error') return;
    const text = message.text();
    if (allowExpected404Console && /404|Failed to load resource/i.test(text)) return;
    consoleErrors.push(text);
  });
  page.on('requestfailed', (request) => {
    const url = new URL(request.url());
    const errorText = request.failure()?.errorText ?? 'unknown';
    // React Query cancels in-flight GET queries (including /api data fetches such as
    // the Power BI report link) when a view unmounts on navigation, surfacing as
    // net::ERR_ABORTED. That is expected teardown, not a failure — matching the abort
    // allowance used by the assignment lifecycle diagnostics.
    const isExpectedNavigationAbort = request.method() === 'GET'
      && errorText === 'net::ERR_ABORTED';
    if (isExpectedNavigationAbort) return;
    failedRequests.push(`${request.method()} ${url.pathname}: ${errorText}`);
  });
  page.on('response', (response) => {
    if (!response.url().includes('/api/') || response.status() < 400) return;
    const key = `${response.request().method()} ${new URL(response.url()).pathname} ${response.status()}`;
    if (!expectedApiFailures.has(key)) failedApiResponses.push(key);
  });

  return {
    expectApiFailure(method, pathname, status) {
      expectedApiFailures.add(`${method} ${pathname} ${status}`);
    },
    setExpected404Console(value) {
      allowExpected404Console = value;
    },
    assertClean() {
      const failures = [
        ...pageErrors.map((value) => `pageerror: ${value}`),
        ...consoleErrors.map((value) => `console: ${value}`),
        ...failedRequests.map((value) => `request: ${value}`),
        ...failedApiResponses.map((value) => `api: ${value}`),
      ];
      if (failures.length > 0) {
        throw new Error(`Wave 1 browser diagnostics failed:\n${failures.join('\n')}`);
      }
    },
  };
}

async function authenticatePage(page, runtime, identity) {
  await page.goto(`${runtime.appUrl}/login`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.evaluate(({ token, email }) => {
    window.localStorage.setItem('authToken', token);
    window.localStorage.setItem('userEmail', email);
  }, { token: identity.token, email: identity.email });
  await page.goto(`${runtime.appUrl}/app`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.locator('#account-menu-button').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
}

function flattenWorksheetEntries(month) {
  return (month?.weeks ?? []).flatMap((week) =>
    (week?.days ?? []).flatMap((day) => day?.entries ?? []));
}

export async function runCustomerWave1Acceptance(viewportName) {
  const runtime = requireRuntime();
  const admin = await getDevIdentity(runtime, 'Admin');
  const user = await getDevIdentity(runtime, 'User');
  const { browser, context, page } = await createBrowser(viewportName);
  const diagnostics = captureBrowserFailures(page);
  const unique = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  const customerName = `Wave 1 kunde ${unique}`;
  const customerEmail = `wave1-${unique}@example.test`;
  const customerPhone = '12345678';
  const updatedCustomerName = `Wave 1 kunde opdateret ${unique}`;
  let customerId = null;
  let jobId = null;

  try {
    await authenticatePage(page, runtime, admin);
    await page.goto(`${runtime.appUrl}/app/customers/new`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    const nameInput = page.locator('#create-customer-name');
    const emailInput = page.locator('#create-customer-email');
    const phoneInput = page.locator('#create-customer-phone');
    await nameInput.fill(customerName);
    await emailInput.fill(customerEmail);
    await page.locator('#create-customer-contact').fill('Wave 1 kontakt');
    await phoneInput.fill(customerPhone);

    const retainedValues = [
      [nameInput, customerName, 'name'],
      [emailInput, customerEmail, 'email'],
      [phoneInput, customerPhone, 'phone'],
    ];
    for (const [field, expected, label] of retainedValues) {
      const actual = await field.inputValue();
      if (actual !== expected) {
        throw new Error(`Customer create ${label} field did not retain its value on ${viewportName}.`);
      }
    }

    const createSubmit = page.locator('#create-customer-submit');
    await createSubmit.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await createSubmit.focus();
    await createSubmit.evaluate((button) => {
      window.__workslipCustomerCreateClicks = 0;
      button.addEventListener('click', () => {
        window.__workslipCustomerCreateClicks += 1;
      }, { once: true });
    });
    const createResponsePromise = page.waitForResponse((response) => {
      return response.request().method() === 'POST'
        && normalizePathname(response.url()) === '/api/customers';
    }, { timeout: API_TIMEOUT });
    await createSubmit.click();
    const createResponse = await createResponsePromise.catch(async () => {
      const state = {
        viewportName,
        name: await nameInput.inputValue().catch(() => null),
        email: await emailInput.inputValue().catch(() => null),
        phone: await phoneInput.inputValue().catch(() => null),
        submitDisabled: await createSubmit.isDisabled().catch(() => null),
        clickCount: await page.evaluate(() => window.__workslipCustomerCreateClicks ?? null).catch(() => null),
      };
      throw new Error(`Customer create produced no POST response. State: ${JSON.stringify(state)}`);
    });
    if (createResponse.status() !== 200) {
      throw new Error(`UI customer create returned HTTP ${createResponse.status()}.`);
    }
    const customer = await createResponse.json();
    customerId = customer?.id ?? null;
    if (!customerId) throw new Error('UI customer create did not return an id.');

    // Open detail by absolute URL. Avoid list search debounce and overlay races.
    await page.goto(`${runtime.appUrl}/app/customers/${customerId}`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    await page.locator('#customer-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const favoriteButton = page.locator('#customer-favorite-button');
    await favoriteButton.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const favoriteResponsePromise = page.waitForResponse((response) => {
      return response.request().method() === 'PATCH'
        && normalizePathname(response.url()) === `/api/customers/${customerId}/favorite`;
    }, { timeout: API_TIMEOUT });
    // Native DOM click avoids FAB/overlay interception on the header control.
    await favoriteButton.evaluate((button) => {
      if (button instanceof HTMLElement) button.click();
    });
    const favoriteResponse = await favoriteResponsePromise;
    if (![200, 204].includes(favoriteResponse.status())) {
      throw new Error(`Favorite mutation returned HTTP ${favoriteResponse.status()}.`);
    }
    const favorited = await apiRequest(runtime, admin, 'GET', `/api/customers/${customerId}`, undefined, [200]);
    if (favorited?.isFavorite !== true) throw new Error('Customer favorite mutation did not persist.');

    await page.goto(`${runtime.appUrl}/app/customers/${customerId}/edit`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    const editNameInput = page.locator('#edit-customer-name');
    await editNameInput.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await editNameInput.fill(updatedCustomerName);
    if (await editNameInput.inputValue() !== updatedCustomerName) {
      throw new Error(`Customer edit name field did not retain its value on ${viewportName}.`);
    }
    const editSave = page.locator('#edit-customer-save');
    await editSave.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await editSave.focus();
    await editSave.evaluate((button) => {
      window.__workslipCustomerEditClicks = 0;
      button.addEventListener('click', () => {
        window.__workslipCustomerEditClicks += 1;
      }, { once: true });
    });
    const editResponsePromise = page.waitForResponse((response) => {
      return response.request().method() === 'PUT'
        && normalizePathname(response.url()) === `/api/customers/${customerId}`;
    }, { timeout: API_TIMEOUT });
    await editSave.click();
    const editResponse = await editResponsePromise.catch(async () => {
      const state = {
        viewportName,
        name: await editNameInput.inputValue().catch(() => null),
        submitDisabled: await editSave.isDisabled().catch(() => null),
        clickCount: await page.evaluate(() => window.__workslipCustomerEditClicks ?? null).catch(() => null),
      };
      throw new Error(`Customer edit produced no PUT response. State: ${JSON.stringify(state)}`);
    });
    if (editResponse.status() !== 200) {
      throw new Error(`UI customer edit returned HTTP ${editResponse.status()}.`);
    }
    await page.locator('#customer-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const updatedCustomer = await apiRequest(runtime, admin, 'GET', `/api/customers/${customerId}`, undefined, [200]);
    if (updatedCustomer?.name !== updatedCustomerName) throw new Error('Customer edit did not persist the updated name.');

    await page.locator('#customer-create-job-button').click();
    await page.waitForURL((url) => url.pathname === '/app/job/new', { timeout: UI_TIMEOUT });
    const routeState = await page.evaluate(() => window.history.state?.usr ?? null);
    if (
      routeState?.fromCustomer !== true
      || routeState?.customerId !== customerId
      || routeState?.customerSnapshot?.name !== updatedCustomerName
    ) {
      throw new Error('Create-job-from-customer navigation did not preserve the expected customer snapshot state.');
    }

    const job = await apiRequest(runtime, admin, 'POST', '/api/jobs/', {
      customerSnapshot: {
        name: updatedCustomerName,
        address: updatedCustomer?.address ?? null,
        email: updatedCustomer?.email ?? null,
        phone: updatedCustomer?.phone ?? null,
        contactPerson: updatedCustomer?.contactPerson ?? null,
      },
      destinationAddress: updatedCustomer?.address ?? 'Testvej 1',
      destinationZipCode: updatedCustomer?.zipCode ?? '8000',
      destinationCity: updatedCustomer?.city ?? 'Aarhus C',
      jobType: 'KLS',
      assignedUserIds: [admin.user.id],
    }, [200], { 'Idempotency-Key': `wave1-customer-job-${unique}` });
    jobId = job?.id ?? null;
    if (!jobId) throw new Error('Customer snapshot verification job did not return an id.');
    const jobBeforeDelete = await apiRequest(runtime, admin, 'GET', `/api/jobs/${jobId}`, undefined, [200]);
    if (readCustomerName(jobBeforeDelete) !== updatedCustomerName) {
      throw new Error('Created job did not persist the updated customer snapshot.');
    }

    await authenticatePage(page, runtime, user);
    await page.goto(`${runtime.appUrl}/app/customers/${customerId}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.locator('#customer-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    if (await page.locator('#customer-favorite-button').count() !== 0) {
      throw new Error('User without customer:edit can see the favorite mutation control.');
    }
    if (await page.locator('#customer-actions-button').count() !== 0) {
      throw new Error('User without customer:edit can see the customer action menu.');
    }
    await page.locator('#customer-create-job-button').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    await apiRequest(runtime, admin, 'DELETE', `/api/customers/${customerId}`, undefined, [204]);
    const jobAfterDelete = await apiRequest(runtime, admin, 'GET', `/api/jobs/${jobId}`, undefined, [200]);
    if (readCustomerName(jobAfterDelete) !== updatedCustomerName) {
      throw new Error('Deleting the customer changed the persisted job customer snapshot.');
    }

    diagnostics.expectApiFailure('GET', `/api/customers/${customerId}`, 404);
    diagnostics.setExpected404Console(true);
    await authenticatePage(page, runtime, admin);
    await page.goto(`${runtime.appUrl}/app/customers/${customerId}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.locator('#customer-detail-error').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    diagnostics.setExpected404Console(false);
    customerId = null;

    await apiRequest(runtime, admin, 'DELETE', `/api/jobs/${jobId}`, undefined, [200, 204, 404]);
    jobId = null;

    diagnostics.assertClean();
  } finally {
    if (jobId) {
      await apiRequest(runtime, admin, 'DELETE', `/api/jobs/${jobId}`, undefined, [200, 204, 404]).catch(() => {});
    }
    if (customerId) {
      await apiRequest(runtime, admin, 'DELETE', `/api/customers/${customerId}`, undefined, [204, 404]).catch(() => {});
    }
    await context.close();
    await browser.close();
  }
}

export async function runWorksheetWave1Acceptance(viewportName) {
  const runtime = requireRuntime();
  const admin = await getDevIdentity(runtime, 'Admin');
  const user = await getDevIdentity(runtime, 'User');
  const { browser, context, page } = await createBrowser(viewportName);
  const diagnostics = captureBrowserFailures(page);
  const unique = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  const workDate = todayInCopenhagen();
  const { year, month } = currentMonthInCopenhagen();
  let jobId = null;
  let worksheetId = null;

  try {
    const job = await apiRequest(runtime, admin, 'POST', '/api/jobs/', {
      customerSnapshot: {
        name: `Wave 1 timer ${unique}`,
        address: 'Testvej 2, 8000 Aarhus C',
        email: `worksheet-${unique}@example.test`,
        phone: '12345678',
        contactPerson: 'Wave 1 kontakt',
      },
      destinationAddress: 'Testvej 2',
      destinationZipCode: '8000',
      destinationCity: 'Aarhus C',
      jobType: 'KLS',
      assignedUserIds: [admin.user.id],
    }, [200], { 'Idempotency-Key': `wave1-job-${unique}` });
    jobId = job?.id;
    if (!jobId) throw new Error('Worksheet acceptance job create did not return an id.');

    const updatedJob = await apiRequest(runtime, admin, 'POST', `/api/worksheets/jobs/${jobId}`, {
      id: null,
      jobId,
      userId: admin.user.id,
      userDisplayName: admin.user.displayName ?? 'Admin',
      workDate,
      hoursWorked: 1.25,
      sleptOnJob: false,
    }, [200]);
    const worksheets = updatedJob?.worksheets ?? updatedJob?.timesheets ?? [];
    worksheetId = worksheets.find((entry) =>
      String(entry?.userId).toLowerCase() === String(admin.user.id).toLowerCase()
      && Number(entry?.hoursWorked) === 1.25
      && String(entry?.workDate).slice(0, 10) === workDate)?.id ?? null;
    if (!worksheetId) throw new Error('Worksheet upsert succeeded but the persisted worksheet could not be resolved.');

    await authenticatePage(page, runtime, admin);
    const waitForTimerLoad = () => page.waitForResponse((response) => {
      const url = new URL(response.url());
      return response.request().method() === 'GET'
        && url.pathname === '/api/worksheets/all'
        && response.status() === 200;
    }, { timeout: API_TIMEOUT });

    let timerLoad = waitForTimerLoad();
    await page.goto(`${runtime.appUrl}/app/timer`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await timerLoad;
    if (new URL(page.url()).pathname !== '/app/timer') throw new Error('Timer route did not open.');

    timerLoad = waitForTimerLoad();
    await page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await timerLoad;
    if (new URL(page.url()).pathname !== '/app/timer') throw new Error('Timer route did not survive reload.');

    await page.goto(`${runtime.appUrl}/app`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    timerLoad = waitForTimerLoad();
    await page.goBack({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await timerLoad;
    if (new URL(page.url()).pathname !== '/app/timer') throw new Error('Timer route did not restore through browser back navigation.');

    const monthData = await apiRequest(runtime, admin, 'GET', `/api/worksheets/all?year=${year}&month=${month}`, undefined, [200]);
    const persistedEntry = flattenWorksheetEntries(monthData).find((entry) =>
      entry?.jobId === jobId && Number(entry?.hoursWorked) === 1.25);
    if (!persistedEntry) throw new Error('Worksheet was not persisted after refresh/back navigation.');

    const emptyPeriod = await apiRequest(runtime, admin, 'GET', '/api/worksheets/all?year=2099&month=1', undefined, [200]);
    if (Number(emptyPeriod?.totalHours) !== 0 || flattenWorksheetEntries(emptyPeriod).length !== 0) {
      throw new Error('Known-empty worksheet period did not return an empty persisted state.');
    }

    await apiRequest(runtime, user, 'GET', `/api/worksheets/my?year=${year}&month=${month}`, undefined, [200]);
    await apiRequest(runtime, user, 'GET', `/api/worksheets/all?year=${year}&month=${month}`, undefined, [403]);
    await apiRequest(runtime, user, 'POST', `/api/worksheets/jobs/${jobId}`, {
      id: null,
      jobId,
      userId: admin.user.id,
      userDisplayName: admin.user.displayName ?? 'Admin',
      workDate,
      hoursWorked: 1.5,
      sleptOnJob: false,
    }, [403]);

    diagnostics.assertClean();
  } finally {
    if (worksheetId && jobId) {
      await apiRequest(runtime, admin, 'DELETE', `/api/worksheets/${worksheetId}/jobs/${jobId}`, undefined, [200, 404]).catch(() => {});
    }
    if (jobId) {
      await apiRequest(runtime, admin, 'DELETE', `/api/jobs/${jobId}`, undefined, [204, 404]).catch(() => {});
    }
    await context.close();
    await browser.close();
  }
}
