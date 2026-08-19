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

async function getDevIdentity(runtime, role) {
  const email = role === 'Admin' ? runtime.adminEmail : runtime.userEmail;
  const response = await fetch(`${runtime.apiUrl}/api/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email }),
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok || !payload?.token || !payload?.user?.id) {
    throw new Error(`Could not resolve synthetic ${role} identity (HTTP ${response.status}).`);
  }
  return { token: payload.token, user: payload.user, email };
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
    failedRequests.push(`${request.method()} ${new URL(request.url()).pathname}: ${request.failure()?.errorText ?? 'unknown'}`);
  });
  page.on('response', (response) => {
    if (!response.url().includes('/api/') || response.status() < 400) return;
    const key = `${response.request().method()} ${new URL(response.url()).pathname} ${response.status()}`;
    if (!expectedApiFailures.delete(key)) failedApiResponses.push(key);
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
  let customerId = null;

  try {
    const customer = await apiRequest(runtime, admin, 'POST', '/api/customers/', {
      name: `Wave 1 kunde ${unique}`,
      customerNumber: `W1-${unique.slice(-8)}`,
      address: 'Testvej 1',
      zipCode: '8000',
      city: 'Aarhus C',
      country: 'Danmark',
      email: `wave1-${unique}@example.test`,
      contactPerson: 'Wave 1 kontakt',
      phone: '12345678',
    }, [200], { 'Idempotency-Key': `wave1-customer-${unique}` });
    customerId = customer?.id;
    if (!customerId) throw new Error('Customer create did not return an id.');

    await authenticatePage(page, runtime, admin);
    await page.goto(`${runtime.appUrl}/app/customers/${customerId}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.locator('#customer-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.locator('#customer-create-job-button').click();
    await page.waitForURL((url) => url.pathname === '/app/job/new', { timeout: UI_TIMEOUT });
    const routeState = await page.evaluate(() => window.history.state?.usr ?? null);
    if (
      routeState?.fromCustomer !== true
      || routeState?.customerId !== customerId
      || routeState?.customerSnapshot?.name !== customer.name
    ) {
      throw new Error('Create-job-from-customer navigation did not preserve the expected customer snapshot state.');
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
    diagnostics.expectApiFailure('GET', `/api/customers/${customerId}`, 404);
    diagnostics.setExpected404Console(true);
    await authenticatePage(page, runtime, admin);
    await page.goto(`${runtime.appUrl}/app/customers/${customerId}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.locator('#customer-detail-error').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    diagnostics.setExpected404Console(false);
    customerId = null;

    diagnostics.assertClean();
  } finally {
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
