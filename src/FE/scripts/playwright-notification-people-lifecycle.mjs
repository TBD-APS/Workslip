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
    auditorEmail: required(
      process.env.WORKSLIP_SYNTHETIC_AUDITOR_EMAIL?.trim() || process.env.WORKSLIP_PLAYWRIGHT_AUDITOR_EMAIL,
      'WORKSLIP_SYNTHETIC_AUDITOR_EMAIL or WORKSLIP_PLAYWRIGHT_AUDITOR_EMAIL',
    ),
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
  return {
    browser,
    context,
    page,
    assertClean() {
      const diagnostics = [
        ...pageErrors.map((message) => `page: ${message}`),
        ...consoleErrors.map((message) => `console: ${message}`),
        ...failedApiRequests.map((message) => `request: ${message}`),
        ...failedApiResponses.map((message) => `response: ${message}`),
      ];
      if (diagnostics.length) {
        throw new Error(`Browser diagnostics failed: ${diagnostics.join(' | ')}`);
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

async function waitForRejectionNotification(runtime, user, rejectionJob) {
  const expectedUrl = `/app/job/${rejectionJob.id}`;
  let lastItems = [];
  for (let attempt = 0; attempt < 60; attempt += 1) {
    const pending = await api(runtime, user, 'GET', '/api/notifications?limit=50', undefined, [200]);
    lastItems = Array.isArray(pending) ? pending : [];
    const matching = lastItems.filter((item) =>
      item?.url === expectedUrl
        && String(item.title ?? '').includes(rejectionJob.reportNumber)
        && String(item.body ?? '').includes(rejectionJob.rejectionNote));
    if (matching.length > 1) {
      throw new Error(`Rejected job ${rejectionJob.id} produced ${matching.length} rejection notifications; expected exactly one.`);
    }
    const [delivered] = matching;
    if (delivered?.id) return { notification: delivered, items: lastItems };
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error(`Rejected job ${rejectionJob.id} did not produce a User rejection notification within 60 seconds.`);
}

function unreadNotificationCount(items) {
  return items.reduce((count, item) => count + (item?.isRead === true ? 0 : 1), 0);
}

async function assertUnreadCount(locator, expected, label) {
  await locator.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const raw = await locator.getAttribute('data-count');
  const actual = raw === null || raw.trim() === '' ? Number.NaN : Number(raw);
  if (!Number.isInteger(actual) || actual < 0 || actual !== expected) {
    throw new Error(`${label}: expected unread count ${expected}, received ${actual}.`);
  }
}

async function verifyNotificationLifecycle(runtime, user, viewportName, rejectionJob) {
  const session = await browserFor(viewportName);
  try {
    const delivered = await waitForRejectionNotification(runtime, user, rejectionJob);
    if (delivered.notification.isRead === true) {
      throw new Error(`Rejection notification for ${rejectionJob.reportNumber} was already read.`);
    }
    const unreadBefore = unreadNotificationCount(delivered.items);

    await authenticate(session.page, runtime, user);
    await assertUnreadCount(
      session.page.locator('#app-notifications-badge'),
      unreadBefore,
      `${viewportName} notification bell`,
    );

    const notificationsResponse = session.page.waitForResponse((response) =>
      response.request().method() === 'GET'
        && new URL(response.url()).pathname === '/api/notifications'
        && response.status() === 200,
    { timeout: API_TIMEOUT });
    await session.page.locator('#app-notifications-button').click();
    const response = await notificationsResponse;
    const items = await response.json();
    const notification = Array.isArray(items)
      ? items.find((item) => item?.id === delivered.notification.id)
      : null;
    if (!notification?.id) {
      throw new Error(`Rejection notification for ${rejectionJob.reportNumber} was not returned to the User.`);
    }
    if (notification.isRead === true || notification.url !== `/app/job/${rejectionJob.id}`) {
      throw new Error(`Rejection notification for ${rejectionJob.reportNumber} had stale read or deep-link state.`);
    }
    if (!String(notification.title ?? '').includes(rejectionJob.reportNumber)
        || !String(notification.body ?? '').includes(rejectionJob.rejectionNote)) {
      throw new Error(`Rejection notification for ${rejectionJob.reportNumber} omitted its job or rejection context.`);
    }
    await assertUnreadCount(
      session.page.locator('#notifications-unread-count'),
      unreadBefore,
      `${viewportName} drawer overview`,
    );
    const notificationRow = session.page.locator(`#notification-row-${notification.id}`);
    await notificationRow.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const notificationRowText = String(await notificationRow.textContent());
    if (!notificationRowText.includes(rejectionJob.reportNumber)
        || !notificationRowText.includes(rejectionJob.rejectionNote)) {
      throw new Error(`Rejection notification for ${rejectionJob.reportNumber} was not rendered with its context.`);
    }

    const openedJobResponse = session.page.waitForResponse((candidate) =>
      candidate.request().method() === 'GET'
        && new URL(candidate.url()).pathname === `/api/jobs/${rejectionJob.id}`
        && candidate.status() === 200,
    { timeout: API_TIMEOUT });
    const markReadResponse = session.page.waitForResponse((candidate) =>
      candidate.request().method() === 'PATCH'
        && new URL(candidate.url()).pathname === `/api/notifications/${notification.id}/read`
        && candidate.status() === 204,
    { timeout: API_TIMEOUT });
    await session.page.locator(`#notification-open-${notification.id}`).click();
    await markReadResponse;
    const openedJob = await (await openedJobResponse).json();
    if (openedJob?.id !== rejectionJob.id) {
      throw new Error(`Rejection notification opened the wrong job on ${viewportName}.`);
    }
    const report = session.page.locator('#job-report-page');
    await report.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const reportText = String(await report.textContent());
    if (!reportText.includes(rejectionJob.reportNumber)
        || !reportText.includes(rejectionJob.rejectionNote)) {
      throw new Error(`Opened job ${rejectionJob.reportNumber} did not render its rejection context on ${viewportName}.`);
    }

    const persisted = await api(runtime, user, 'GET', '/api/notifications?limit=50', undefined, [200]);
    const persistedItem = Array.isArray(persisted)
      ? persisted.find((item) => item.id === notification.id)
      : null;
    if (persistedItem?.isRead !== true) {
      throw new Error(`Read state did not persist for ${viewportName} rejection notification.`);
    }
    const unreadAfter = unreadNotificationCount(Array.isArray(persisted) ? persisted : []);
    if (unreadAfter !== unreadBefore - 1) {
      throw new Error(`${viewportName} unread count did not decrement after opening the rejection notification.`);
    }

    const reloadNotifications = session.page.waitForResponse((candidate) =>
      candidate.request().method() === 'GET'
        && new URL(candidate.url()).pathname === '/api/notifications'
        && candidate.status() === 200,
    { timeout: API_TIMEOUT });
    await session.page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await reloadNotifications;
    await session.page.locator('#account-menu-button').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    if (unreadAfter === 0) {
      if (await session.page.locator('#app-notifications-badge').count() !== 0) {
        throw new Error(`${viewportName} notification badge remained visible with no unread events after reload.`);
      }
    } else {
      await assertUnreadCount(
        session.page.locator('#app-notifications-badge'),
        unreadAfter,
        `${viewportName} notification bell after reload`,
      );
    }

    const reloadedDrawerResponse = session.page.waitForResponse((candidate) =>
      candidate.request().method() === 'GET'
        && new URL(candidate.url()).pathname === '/api/notifications'
        && candidate.status() === 200,
    { timeout: API_TIMEOUT });
    await session.page.locator('#app-notifications-button').click();
    await reloadedDrawerResponse;
    await assertUnreadCount(
      session.page.locator('#notifications-unread-count'),
      unreadAfter,
      `${viewportName} drawer after reload`,
    );
    const reloadedRow = session.page.locator(`#notification-row-${notification.id}`);
    await reloadedRow.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    if ((await reloadedRow.getAttribute('class'))?.includes('notification-item-unread')) {
      throw new Error(`Read state was stale in the reloaded ${viewportName} drawer.`);
    }

    session.assertClean();
  } finally {
    await session.context.close();
    await session.browser.close();
  }
}

async function verifyNotificationPermissionBoundary(runtime, auditor, viewportName) {
  const session = await browserFor(viewportName);
  try {
    await authenticate(session.page, runtime, auditor);
    if (await session.page.locator('#app-notifications-button').count() !== 0) {
      throw new Error(`Auditor shell exposed notifications on ${viewportName}.`);
    }

    const response = await fetch(`${runtime.apiUrl}/api/notifications`, {
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${auditor.token}`,
      },
      signal: AbortSignal.timeout(API_TIMEOUT),
    });
    if (response.status !== 403) {
      throw new Error(`Auditor notifications API returned ${response.status}; expected 403.`);
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

async function createRejectedDiverseJob(runtime, admin, user, unique, viewportName, cleanupJobIds) {
  const label = `Notification rejection ${viewportName}`;
  const job = await api(runtime, user, 'POST', '/api/jobs/', {
    customerSnapshot: {
      name: `${label} ${unique}`,
      address: 'Testvej 7, 8000 Aarhus C',
      email: `notification-${viewportName}-${unique}@example.test`,
      phone: '12345678',
      contactPerson: 'Browser coverage',
    },
    destinationAddress: 'Testvej 7',
    destinationZipCode: '8000',
    destinationCity: 'Aarhus C',
    jobType: 'Diverse',
    assignedUserIds: [user.user.id],
  }, [200], { 'Idempotency-Key': `notification-rejection-create-${viewportName}-${unique}` });
  const id = job?.id ?? null;
  const reportNumber = String(job?.reportNumber ?? '').trim();
  if (!id || !reportNumber) throw new Error(`${label} fixture did not return id/reportNumber.`);
  cleanupJobIds.push(id);

  const submitted = await api(runtime, user, 'POST', `/api/jobs/${id}/status`, {
    status: 'InReview',
  }, [200], { 'Idempotency-Key': `notification-rejection-submit-${viewportName}-${unique}` });
  if (String(submitted?.status ?? '').toLowerCase() !== 'inreview') {
    throw new Error(`${label} fixture did not transition Draft -> InReview.`);
  }

  const rejectionNote = `Playwright ${viewportName}: ret dokumentationen.`;
  const rejected = await api(runtime, admin, 'POST', `/api/jobs/${id}/status`, {
    status: 'Rejected',
    rejectionNote,
  }, [200], { 'Idempotency-Key': `notification-rejection-reject-${viewportName}-${unique}` });
  if (String(rejected?.status ?? '').toLowerCase() !== 'rejected'
      || rejected?.rejectionNote !== rejectionNote) {
    throw new Error(`${label} fixture did not transition InReview -> Rejected with its reason.`);
  }

  return { ...job, id, reportNumber, rejectionNote };
}

async function main() {
  const runtime = requireRuntime();
  const admin = await identity(runtime, runtime.adminEmail, 'Admin');
  const user = await identity(runtime, runtime.userEmail, 'User');
  const auditor = await identity(runtime, runtime.auditorEmail, 'Auditor');
  const originalUser = await api(runtime, admin, 'GET', `/api/users/${user.user.id}`, undefined, [200]);
  const updatedName = `${originalUser.displayName} browser coverage`;
  const unique = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  let assignmentJobId = null;
  const notificationJobIds = [];

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

    for (const viewportName of ['desktop', 'mobile']) {
      const notificationJob = await createRejectedDiverseJob(
        runtime,
        admin,
        user,
        `${unique}-${viewportName}`,
        viewportName,
        notificationJobIds,
      );
      await verifyNotificationLifecycle(runtime, user, viewportName, notificationJob);
      await verifyNotificationPermissionBoundary(runtime, auditor, viewportName);
    }

    console.log('Notification + people lifecycle browser coverage passed on desktop and mobile, including rejection inbox, Auditor denial, and people-page assignment/unassignment.');
  } finally {
    await api(runtime, admin, 'PATCH', `/api/users/${user.user.id}`, {
      displayName: originalUser.displayName ?? null,
      phone: originalUser.phone ?? null,
      role: originalUser.role ?? 'User',
    }, [200]).catch(() => {});
    if (assignmentJobId) {
      await api(runtime, admin, 'DELETE', `/api/jobs/${assignmentJobId}`, undefined, [200, 204, 404]).catch(() => {});
    }
    for (const notificationJobId of notificationJobIds) {
      await api(runtime, admin, 'DELETE', `/api/jobs/${notificationJobId}`, undefined, [200, 204, 404]).catch(() => {});
    }
  }
}

await main();
