import assert from 'node:assert/strict';
import process from 'node:process';

const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;
const MOBILE_DEVICE_NAME = 'iPhone 13';
const DESKTOP_VIEWPORTS = {
  desktop: { width: 1280, height: 800 },
  'desktop-wide': { width: 1440, height: 900 },
};

export async function runTimerLedgerAcceptance(viewportName) {
  const runtime = requireRuntime();
  const admin = await getDevIdentity(runtime);
  const { chromium, devices } = await import('playwright');
  const browser = await chromium.launch({ headless: true });
  const desktopViewport = DESKTOP_VIEWPORTS[viewportName];
  const isDesktop = Boolean(desktopViewport);
  const context = await browser.newContext({
    ...(isDesktop ? { viewport: desktopViewport } : devices[MOBILE_DEVICE_NAME]),
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  await context.route(
    (url) => !['127.0.0.1', 'localhost', '::1'].includes(url.hostname),
    (route) => route.fulfill({ status: 204, contentType: 'application/javascript', body: '' }),
  );

  const page = await context.newPage();
  const pageErrors = [];
  const consoleErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });

  const unique = `${Date.now()}-${viewportName}`;
  const workDate = todayInCopenhagen();
  let jobId = null;
  let worksheetId = null;

  try {
    const job = await apiRequest(runtime, admin, 'POST', '/api/jobs/', {
      customerSnapshot: {
        name: `Timer ledger ${unique}`,
        address: 'Ledgervej 7, 8000 Aarhus C',
        email: `timer-ledger-${unique}@example.test`,
        phone: '12345678',
        contactPerson: 'Timer Ledger',
      },
      destinationAddress: 'Ledgervej 7',
      destinationZipCode: '8000',
      destinationCity: 'Aarhus C',
      jobType: 'KLS',
      assignedUserIds: [admin.user.id],
    }, [200], { 'Idempotency-Key': `timer-ledger-job-${unique}` });
    jobId = job?.id ?? null;
    if (!jobId) throw new Error('Timer ledger fixture did not return a job id.');

    const updatedJob = await apiRequest(runtime, admin, 'POST', `/api/worksheets/jobs/${jobId}`, {
      id: null,
      jobId,
      userId: admin.user.id,
      userDisplayName: admin.user.displayName ?? 'Admin',
      workDate,
      hoursWorked: 2.5,
      sleptOnJob: false,
    }, [200]);
    const worksheets = updatedJob?.worksheets ?? updatedJob?.timesheets ?? [];
    worksheetId = worksheets.find((entry) =>
      String(entry?.userId).toLowerCase() === String(admin.user.id).toLowerCase()
      && Number(entry?.hoursWorked) === 2.5
      && String(entry?.workDate).slice(0, 10) === workDate)?.id ?? null;
    if (!worksheetId) throw new Error('Timer ledger worksheet fixture could not be resolved after persistence.');

    const { year, month } = currentMonthInCopenhagen();
    const monthData = await apiRequest(runtime, admin, 'GET', `/api/worksheets/all?year=${year}&month=${month}`, undefined, [200]);
    const fixtureWeek = (monthData?.weeks ?? []).find((week) =>
      (week?.days ?? []).some((day) =>
        (day?.entries ?? []).some((entry) => entry?.jobId === jobId && String(entry?.userId).toLowerCase() === String(admin.user.id).toLowerCase())));
    if (!fixtureWeek?.weekStart) throw new Error('Timer ledger fixture week could not be resolved from the persisted monthly overview.');

    await authenticateTimerPage(page, runtime, admin);
    await page.locator('#timer-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.locator('#timer-summary').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const ledgerEntryId = `#timer-ledger-entry-${jobId}-${admin.user.id}-${workDate}`;
    if (isDesktop) {
      await page.locator('#timer-ledger').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await page.locator(ledgerEntryId).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      assert.equal(await page.locator('#timer-view-ledger').getAttribute('aria-pressed'), 'true');
      assert.equal(await page.locator('#timer-mobile-overview').isVisible(), false, 'Desktop Timer must not show the mobile card overview.');

      await page.locator('#timer-view-week').click();
      await page.locator('#timer-week-overview').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      assert.equal(await page.locator('#timer-view-week').getAttribute('aria-pressed'), 'true');
      await page.locator(`#timer-week-matrix-${fixtureWeek.weekStart}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });

      await page.locator('#timer-view-ledger').click();
      await page.locator(ledgerEntryId).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    } else {
      await page.locator('#timer-mobile-overview').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await page.locator(`#timer-mobile-week-${fixtureWeek.weekStart}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      assert.equal(await page.locator('#timer-ledger').isVisible(), false, 'Mobile Timer must not expose the desktop ledger visually.');
      assert.equal(await page.locator('#timer-view-ledger').isVisible(), false, 'Mobile Timer must not expose the desktop admin view switcher visually.');
    }

    const dimensions = await page.evaluate(() => ({
      documentWidth: document.documentElement.scrollWidth,
      viewportWidth: window.innerWidth,
    }));
    assert.ok(
      dimensions.documentWidth <= dimensions.viewportWidth,
      `${viewportName}: Timer redesign introduced horizontal page overflow (${dimensions.documentWidth} > ${dimensions.viewportWidth}).`,
    );
    assert.deepEqual(pageErrors, [], `${viewportName}: Timer page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${viewportName}: Timer console errors: ${consoleErrors.join(' | ')}`);
  } finally {
    if (worksheetId && jobId) {
      await apiRequest(runtime, admin, 'DELETE', `/api/worksheets/${worksheetId}/jobs/${jobId}`, undefined, [200, 204, 404]).catch(() => {});
    }
    if (jobId) {
      await apiRequest(runtime, admin, 'DELETE', `/api/jobs/${jobId}`, undefined, [200, 204, 404]).catch(() => {});
    }
    await context.close();
    await browser.close();
  }
}

function requireRuntime() {
  if (process.env.WORKSLIP_ALLOW_LOCAL_DEV_TOKEN !== 'true') {
    throw new Error('WORKSLIP_ALLOW_LOCAL_DEV_TOKEN must be exactly true for Timer ledger acceptance.');
  }
  return {
    appUrl: requireLoopbackOrigin(process.env.WORKSLIP_LOCAL_APP_URL, 'WORKSLIP_LOCAL_APP_URL'),
    apiUrl: requireLoopbackOrigin(process.env.WORKSLIP_LOCAL_API_URL, 'WORKSLIP_LOCAL_API_URL'),
    adminEmail: requireValue(process.env.WORKSLIP_SYNTHETIC_ADMIN_EMAIL, 'WORKSLIP_SYNTHETIC_ADMIN_EMAIL'),
  };
}

function requireLoopbackOrigin(value, name) {
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

async function getDevIdentity(runtime) {
  const response = await fetch(`${runtime.apiUrl}/api/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email: runtime.adminEmail }),
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok || !payload?.token) {
    throw new Error(`Could not issue synthetic Admin token (HTTP ${response.status}).`);
  }
  const identity = { token: payload.token, email: runtime.adminEmail, user: payload.user };
  const me = await apiRequest(runtime, identity, 'GET', '/api/auth/me', undefined, [200]);
  if (!me?.id || String(me.role ?? '').toLowerCase() !== 'admin') {
    throw new Error('Synthetic Timer acceptance identity did not resolve to Admin.');
  }
  return { ...identity, user: me };
}

async function authenticateTimerPage(page, runtime, identity) {
  await page.goto(`${runtime.appUrl}/login`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.evaluate(({ token, email }) => {
    localStorage.setItem('authToken', token);
    localStorage.setItem('userEmail', email);
  }, { token: identity.token, email: identity.email });
  await page.goto(`${runtime.appUrl}/app/timer`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
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

function todayInCopenhagen() {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Copenhagen',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
}
