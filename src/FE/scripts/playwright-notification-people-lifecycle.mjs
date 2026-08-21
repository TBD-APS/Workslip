import process from 'node:process';

const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;
const MOBILE_DEVICE_NAME = 'iPhone 13';

function requireRuntime() {
  if (process.env.WORKSLIP_ALLOW_LOCAL_DEV_TOKEN !== 'true') {
    throw new Error('WORKSLIP_ALLOW_LOCAL_DEV_TOKEN must be exactly true.');
  }
  return {
    appUrl: loopbackOrigin(process.env.WORKSLIP_LOCAL_APP_URL, 'WORKSLIP_LOCAL_APP_URL'),
    apiUrl: loopbackOrigin(process.env.WORKSLIP_LOCAL_API_URL, 'WORKSLIP_LOCAL_API_URL'),
    adminEmail: required(process.env.WORKSLIP_SYNTHETIC_ADMIN_EMAIL, 'WORKSLIP_SYNTHETIC_ADMIN_EMAIL'),
    userEmail: required(process.env.WORKSLIP_SYNTHETIC_USER_EMAIL, 'WORKSLIP_SYNTHETIC_USER_EMAIL'),
  };
}

function required(value, name) {
  const normalized = String(value ?? '').trim();
  if (!normalized) throw new Error(`${name} is required.`);
  return normalized;
}

function loopbackOrigin(value, name) {
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

async function identity(runtime, email, expectedRole) {
  const tokenResponse = await fetch(`${runtime.apiUrl}/api/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email }),
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  const tokenPayload = await tokenResponse.json().catch(() => null);
  if (!tokenResponse.ok || !tokenPayload?.token) {
    throw new Error(`Could not issue ${expectedRole} dev token (HTTP ${tokenResponse.status}).`);
  }
  const partial = { token: tokenPayload.token, email };
  const user = await api(runtime, partial, 'GET', '/api/auth/me', undefined, [200]);
  if (!user?.id || String(user.role ?? '').toLowerCase() !== expectedRole.toLowerCase()) {
    throw new Error(`Could not resolve ${expectedRole} identity.`);
  }
  return { ...partial, user };
}

async function api(runtime, actor, method, pathname, body, expectedStatuses, extraHeaders = {}) {
  const headers = {
    Accept: 'application/json',
    Authorization: `Bearer ${actor.token}`,
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

async function browserFor(viewportName) {
  const { chromium, devices } = await import('playwright');
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    ...(viewportName === 'desktop' ? { viewport: { width: 1280, height: 800 } } : devices[MOBILE_DEVICE_NAME]),
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  const page = await context.newPage();
  const pageErrors = [];
  const consoleErrors = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  return {
    browser,
    context,
    page,
    assertClean() {
      if (pageErrors.length || consoleErrors.length) {
        throw new Error(`Browser diagnostics failed: ${[...pageErrors, ...consoleErrors].join(' | ')}`);
      }
    },
  };
}

async function authenticate(page, runtime, actor) {
  await page.goto(`${runtime.appUrl}/login`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.evaluate(({ token, email }) => {
    window.localStorage.setItem('authToken', token);
    window.localStorage.setItem('userEmail', email);
  }, { token: actor.token, email: actor.email });
  await page.goto(`${runtime.appUrl}/app`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.locator('#account-menu-button').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
}

async function waitForBodyText(page, expected) {
  await page.waitForFunction(
    (value) => document.body.textContent?.includes(value) === true,
    expected,
    { timeout: UI_TIMEOUT },
  );
}

async function openPeopleDetail(page, runtime, userId) {
  const detailResponse = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === `/api/users/${userId}`
      && response.status() === 200,
  { timeout: API_TIMEOUT });
  await page.goto(`${runtime.appUrl}/app/users/${userId}`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  await detailResponse;
  await page.locator('#user-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
}

async function ensureAssignedJobsOpen(page) {
  const trigger = page.locator('#user-assigned-jobs-trigger');
  await trigger.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  if (await trigger.getAttribute('aria-expanded') !== 'true') {
    await trigger.click();
  }
}

function assignedUserIds(job) {
  return Array.isArray(job?.assignedUsers)
    ? job.assignedUsers.map((candidate) => candidate?.id).filter(Boolean).sort()
    : [];
}

async function assertJobAssignees(runtime, admin, jobId, expectedIds, label) {
  const job = await api(runtime, admin, 'GET', `/api/jobs/${jobId}`, undefined, [200]);
  const actual = assignedUserIds(job);
  const expected = [...expectedIds].sort();
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${label}: expected assignees ${expected.join(', ')}, received ${actual.join(', ')}.`);
  }
  return job;
}

async function verifyPeopleLifecycle(runtime, admin, user, viewportName, expectedName, assignmentJob) {
  const session = await browserFor(viewportName);
  const userId = user.user.id;
  const jobId = assignmentJob.id;
  const jobPath = `/api/jobs/${jobId}/assign`;
  try {
    await authenticate(session.page, runtime, admin);
    await openPeopleDetail(session.page, runtime, userId);

    const name = session.page.locator('#user-detail-name');
    const email = session.page.locator('#user-detail-email');
    if (await name.textContent() !== expectedName) {
      throw new Error(`People detail did not show the persisted display name for ${viewportName}.`);
    }
    if (await email.textContent() !== user.user.email) {
      throw new Error(`People detail did not show the expected email for ${viewportName}.`);
    }

    await ensureAssignedJobsOpen(session.page);
    if (await session.page.locator(`#user-assigned-job-${jobId}`).count() !== 0) {
      throw new Error(`People assignment fixture was already assigned before the ${viewportName} UI action.`);
    }

    const search = session.page.locator('#user-job-search');
    await search.fill(assignmentJob.reportNumber);
    await session.page.locator(`#user-assignable-job-${jobId}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const assignResponse = session.page.waitForResponse((response) =>
      response.request().method() === 'POST'
        && new URL(response.url()).pathname === jobPath
        && response.status() >= 200
        && response.status() < 300,
    { timeout: API_TIMEOUT });
    await session.page.locator(`#user-job-assignment-action-${jobId}`).click();
    await assignResponse;

    await assertJobAssignees(
      runtime,
      admin,
      jobId,
      [admin.user.id, userId],
      `${viewportName} assign from people detail`,
    );

    await ensureAssignedJobsOpen(session.page);
    const assignedCard = session.page.locator(`#user-assigned-job-${jobId}`);
    await assignedCard.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    await session.page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await session.page.locator('#user-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await ensureAssignedJobsOpen(session.page);
    await session.page.locator(`#user-assigned-job-${jobId}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    await session.page.locator(`#user-assigned-job-${jobId}`).click();
    await session.page.waitForURL((url) => url.pathname === `/app/job/${jobId}`, { timeout: UI_TIMEOUT });

    await openPeopleDetail(session.page, runtime, userId);
    await session.page.locator('#user-job-search').fill(assignmentJob.reportNumber);
    await session.page.locator(`#user-assignable-job-${jobId}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const removeResponse = session.page.waitForResponse((response) =>
      response.request().method() === 'POST'
        && new URL(response.url()).pathname === jobPath
        && response.status() >= 200
        && response.status() < 300,
    { timeout: API_TIMEOUT });
    await session.page.locator(`#user-job-assignment-action-${jobId}`).click();
    await removeResponse;

    await assertJobAssignees(
      runtime,
      admin,
      jobId,
      [admin.user.id],
      `${viewportName} unassign from people detail`,
    );

    await ensureAssignedJobsOpen(session.page);
    await session.page.locator(`#user-assigned-job-${jobId}`).waitFor({ state: 'detached', timeout: UI_TIMEOUT });

    await session.page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await session.page.locator('#user-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await ensureAssignedJobsOpen(session.page);
    if (await session.page.locator(`#user-assigned-job-${jobId}`).count() !== 0) {
      throw new Error(`People unassignment did not persist after ${viewportName} reload.`);
    }

    session.assertClean();
  } finally {
    await session.context.close();
    await session.browser.close();
  }
}

async function verifyPeoplePermissionBoundary(runtime, user, viewportName) {
  const session = await browserFor(viewportName);
  try {
    await authenticate(session.page, runtime, user);
    await session.page.goto(`${runtime.appUrl}/app/users/${user.user.id}`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    await session.page.waitForFunction(
      (userId) => window.location.pathname !== `/app/users/${userId}`,
      user.user.id,
      { timeout: UI_TIMEOUT },
    );
    if (await session.page.locator('#user-job-search').count() !== 0) {
      throw new Error(`Regular User exposed Admin people-assignment controls on ${viewportName}.`);
    }
    session.assertClean();
  } finally {
    await session.context.close();
    await session.browser.close();
  }
}

async function verifyNotificationLifecycle(runtime, user, viewportName, reportNumber, markRead) {
  const session = await browserFor(viewportName);
  try {
    await authenticate(session.page, runtime, user);
    // The assignment notification is delivered asynchronously by the push
    // worker, so poll the API until it lands before asserting the UI. Without
    // this the panel can open before delivery and the lookup races (flaky).
    for (let attempt = 0; attempt < 60; attempt += 1) {
      const pending = await api(runtime, user, 'GET', '/api/notifications', undefined, [200]);
      const delivered = Array.isArray(pending)
        && pending.some((item) => String(item?.jobNumber ?? item?.title ?? '').includes(reportNumber));
      if (delivered) break;
      await new Promise((resolve) => setTimeout(resolve, 1000));
    }
    const notificationsResponse = session.page.waitForResponse((response) =>
      response.request().method() === 'GET'
        && new URL(response.url()).pathname === '/api/notifications'
        && response.status() === 200,
    { timeout: API_TIMEOUT });
    await session.page.locator('#app-notifications-button').click();
    const response = await notificationsResponse;
    const items = await response.json();
    const notification = Array.isArray(items)
      ? items.find((item) => String(item?.jobNumber ?? item?.title ?? '').includes(reportNumber))
      : null;
    if (!notification?.id) {
      throw new Error(`Assignment notification for ${reportNumber} was not returned to the User.`);
    }
    await waitForBodyText(session.page, reportNumber);

    if (markRead && notification.isRead !== true) {
      const markReadResponse = session.page.waitForResponse((candidate) =>
        candidate.request().method() === 'POST'
          && new URL(candidate.url()).pathname === '/api/notifications/read-all'
          && candidate.status() >= 200
          && candidate.status() < 300,
      { timeout: API_TIMEOUT });
      const clicked = await session.page.evaluate(() => {
        const button = [...document.querySelectorAll('button')]
          .find((candidate) => candidate.textContent?.includes('Marker alle læst'));
        if (!(button instanceof HTMLButtonElement)) return false;
        button.click();
        return true;
      });
      if (!clicked) throw new Error('Notifications drawer did not expose the mark-all-read action.');
      await markReadResponse;
      const persisted = await api(runtime, user, 'GET', '/api/notifications?limit=50', undefined, [200]);
      const persistedItem = Array.isArray(persisted)
        ? persisted.find((item) => item.id === notification.id)
        : null;
      if (persistedItem?.isRead !== true) {
        throw new Error('Mark-all-read did not persist for the assignment notification.');
      }
    }

    session.assertClean();
  } finally {
    await session.context.close();
    await session.browser.close();
  }
}

async function createJob(runtime, admin, unique, label, assignedUserIds) {
  const job = await api(runtime, admin, 'POST', '/api/jobs/', {
    customerSnapshot: {
      name: `${label} ${unique}`,
      address: 'Testvej 3, 8000 Aarhus C',
      email: `${label.toLowerCase().replace(/\s+/g, '-')}-${unique}@example.test`,
      phone: '12345678',
      contactPerson: 'Browser coverage',
    },
    destinationAddress: 'Testvej 3',
    destinationZipCode: '8000',
    destinationCity: 'Aarhus C',
    jobType: 'KLS',
    assignedUserIds,
  }, [200], { 'Idempotency-Key': `notif-people-${label.toLowerCase().replace(/\s+/g, '-')}-${unique}` });
  const id = job?.id ?? null;
  const reportNumber = String(job?.reportNumber ?? '').trim();
  if (!id || !reportNumber) throw new Error(`${label} fixture job did not return id/reportNumber.`);
  return { ...job, id, reportNumber };
}

async function main() {
  const runtime = requireRuntime();
  const admin = await identity(runtime, runtime.adminEmail, 'Admin');
  const user = await identity(runtime, runtime.userEmail, 'User');
  const originalUser = await api(runtime, admin, 'GET', `/api/users/${user.user.id}`, undefined, [200]);
  const updatedName = `${originalUser.displayName} browser coverage`;
  const unique = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  let assignmentJobId = null;
  let notificationJobId = null;

  try {
    await api(runtime, admin, 'PATCH', `/api/users/${user.user.id}`, {
      displayName: updatedName,
      phone: originalUser.phone ?? null,
      role: originalUser.role ?? 'User',
    }, [200]);
    const persistedUser = await api(runtime, admin, 'GET', `/api/users/${user.user.id}`, undefined, [200]);
    if (persistedUser?.displayName !== updatedName) {
      throw new Error('People lifecycle update did not persist.');
    }

    const assignmentJob = await createJob(
      runtime,
      admin,
      unique,
      'People assignment coverage',
      [admin.user.id],
    );
    assignmentJobId = assignmentJob.id;
    await assertJobAssignees(runtime, admin, assignmentJob.id, [admin.user.id], 'Initial people assignment fixture');

    await verifyPeopleLifecycle(runtime, admin, user, 'desktop', updatedName, assignmentJob);
    await verifyPeopleLifecycle(runtime, admin, user, 'mobile', updatedName, assignmentJob);
    await verifyPeoplePermissionBoundary(runtime, user, 'desktop');
    await verifyPeoplePermissionBoundary(runtime, user, 'mobile');

    const notificationJob = await createJob(
      runtime,
      admin,
      `${unique}-notification`,
      'Notification coverage',
      [user.user.id],
    );
    notificationJobId = notificationJob.id;

    await verifyNotificationLifecycle(runtime, user, 'desktop', notificationJob.reportNumber, true);
    await verifyNotificationLifecycle(runtime, user, 'mobile', notificationJob.reportNumber, false);

    console.log('Notification + people lifecycle browser coverage passed on desktop and mobile, including people-page assignment/unassignment.');
  } finally {
    await api(runtime, admin, 'PATCH', `/api/users/${user.user.id}`, {
      displayName: originalUser.displayName ?? null,
      phone: originalUser.phone ?? null,
      role: originalUser.role ?? 'User',
    }, [200]).catch(() => {});
    if (assignmentJobId) {
      await api(runtime, admin, 'DELETE', `/api/jobs/${assignmentJobId}`, undefined, [200, 204, 404]).catch(() => {});
    }
    if (notificationJobId) {
      await api(runtime, admin, 'DELETE', `/api/jobs/${notificationJobId}`, undefined, [200, 204, 404]).catch(() => {});
    }
  }
}

await main();
