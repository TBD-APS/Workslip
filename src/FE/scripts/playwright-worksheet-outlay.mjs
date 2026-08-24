import assert from 'node:assert/strict';
import process from 'node:process';

const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;
const MOBILE_DEVICE_NAME = 'iPhone 13';

export async function runWorksheetOutlayAcceptance(viewportName) {
  const runtime = requireRuntime();
  const admin = await getDevIdentity(runtime);
  const { chromium, devices } = await import('playwright');
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    ...(viewportName === 'desktop'
      ? { viewport: { width: 1280, height: 800 } }
      : devices[MOBILE_DEVICE_NAME]),
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  const page = await context.newPage();
  const unique = `${Date.now()}-${viewportName}`;
  const workDate = todayInCopenhagen();
  const { year, month } = currentMonthInCopenhagen();
  let jobId = null;
  let worksheetId = null;

  try {
    // First prove the actual UI control toggles after editing the hours field. This
    // covers the mobile focus/tap path that has regressed before.
    await authenticatePage(page, runtime, admin);
    await page.goto(`${runtime.appUrl}/app/job/simple/new`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.locator('#job-worksheet-add-trigger').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await page.locator('#job-worksheet-add-trigger').click();
    await page.locator('#worksheet-add-hours').fill('1,25');
    const outlayToggle = page.locator('#worksheet-add-outlay');
    await outlayToggle.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await outlayToggle.getAttribute('aria-checked'), 'false');
    await outlayToggle.click();
    assert.equal(await outlayToggle.getAttribute('aria-checked'), 'true', `${viewportName}: Udlæg toggle did not stay selected.`);

    // Then prove the server contract and read projections preserve the flag.
    const job = await apiRequest(runtime, admin, 'POST', '/api/jobs/', {
      customerSnapshot: {
        name: `Udlæg regression ${unique}`,
        address: 'Udlægsvej 1, 8000 Aarhus C',
        email: `outlay-${unique}@example.test`,
        phone: '12345678',
        contactPerson: 'Udlæg regression',
      },
      destinationAddress: 'Udlægsvej 1',
      destinationZipCode: '8000',
      destinationCity: 'Aarhus C',
      jobType: 'KLS',
      assignedUserIds: [admin.user.id],
    }, [200], { 'Idempotency-Key': `outlay-job-${unique}` });
    jobId = job?.id ?? null;
    if (!jobId) throw new Error('Outlay regression job did not return an id.');

    const updatedJob = await apiRequest(runtime, admin, 'POST', `/api/worksheets/jobs/${jobId}`, {
      id: null,
      jobId,
      userId: admin.user.id,
      userDisplayName: admin.user.displayName ?? 'Admin',
      workDate,
      hoursWorked: 1.25,
      sleptOnJob: true,
    }, [200]);

    const worksheets = updatedJob?.worksheets ?? updatedJob?.timesheets ?? [];
    const persistedWorksheet = worksheets.find((entry) =>
      String(entry?.userId).toLowerCase() === String(admin.user.id).toLowerCase()
      && Number(entry?.hoursWorked) === 1.25
      && String(entry?.workDate).slice(0, 10) === workDate);
    worksheetId = persistedWorksheet?.id ?? null;
    if (!worksheetId) throw new Error('Outlay worksheet could not be resolved after persistence.');
    assert.equal(persistedWorksheet.sleptOnJob, true, 'Job summary lost the outlay flag.');
    assert.equal(Number(updatedJob?.totalOutlay), 1, 'Job summary did not count the outlay.');

    const monthData = await apiRequest(runtime, admin, 'GET', `/api/worksheets/all?year=${year}&month=${month}`, undefined, [200]);
    const day = (monthData?.weeks ?? [])
      .flatMap((week) => week?.days ?? [])
      .find((candidate) => (candidate?.entries ?? []).some((entry) => entry?.jobId === jobId));
    const monthEntry = (day?.entries ?? []).find((entry) => entry?.jobId === jobId);
    if (!monthEntry) throw new Error('Outlay worksheet was missing from the monthly projection.');
    assert.equal(monthEntry.hasOutlay, true, 'Monthly projection lost the outlay flag.');
    assert.ok(Number(day?.outlayCount) >= 1, 'Day projection did not count the outlay.');

    // Finally prove the customer-visible Timer surface renders the persisted value.
    await page.goto(`${runtime.appUrl}/app/timer`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    if (viewportName === 'desktop') {
      const row = page.locator(`#timer-ledger-entry-${jobId}-${admin.user.id}-${workDate}`);
      await row.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      const outlayCell = row.locator('.timer-ledger-outlay');
      await outlayCell.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      assert.match((await outlayCell.innerText()).trim(), /Ja/i, 'Timer ledger did not render the persisted outlay.');
    } else {
      await page.locator('#timer-mobile-overview').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      const body = await page.locator('#timer-mobile-overview').innerText();
      assert.match(body, /Udlæg/i, 'Mobile Timer did not expose outlay information.');
    }
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
    throw new Error('WORKSLIP_ALLOW_LOCAL_DEV_TOKEN must be exactly true for outlay acceptance.');
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
  if (!response.ok || !payload?.token) throw new Error(`Could not issue synthetic Admin token (HTTP ${response.status}).`);
  const identity = { token: payload.token, email: runtime.adminEmail, user: payload.user };
  const me = await apiRequest(runtime, identity, 'GET', '/api/auth/me', undefined, [200]);
  if (!me?.id) throw new Error('Synthetic outlay identity did not resolve through /api/auth/me.');
  return { ...identity, user: me };
}

async function authenticatePage(page, runtime, identity) {
  await page.goto(`${runtime.appUrl}/login`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.evaluate(({ token, email }) => {
    localStorage.setItem('authToken', token);
    localStorage.setItem('userEmail', email);
  }, { token: identity.token, email: identity.email });
  await page.goto(`${runtime.appUrl}/app`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.locator('#account-menu-button').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
}

async function apiRequest(runtime, identity, method, pathname, body, expectedStatuses, extraHeaders = {}) {
  const headers = { Accept: 'application/json', Authorization: `Bearer ${identity.token}`, ...extraHeaders };
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
    timeZone: 'Europe/Copenhagen', year: 'numeric', month: 'numeric',
  }).formatToParts(new Date());
  return {
    year: Number(parts.find((part) => part.type === 'year')?.value),
    month: Number(parts.find((part) => part.type === 'month')?.value),
  };
}

function todayInCopenhagen() {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Copenhagen', year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(new Date());
}
