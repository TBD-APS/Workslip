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

async function api(runtime, actor, method, pathname, body, expectedStatuses) {
  const headers = {
    Accept: 'application/json',
    Authorization: `Bearer ${actor.token}`,
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

async function verifyPeopleLifecycle(runtime, admin, user, viewportName, expectedName) {
  const session = await browserFor(viewportName);
  try {
    await authenticate(session.page, runtime, admin);
    const detailResponse = session.page.waitForResponse((response) =>
      response.request().method() === 'GET'
        && new URL(response.url()).pathname === `/api/users/${user.user.id}`
        && response.status() === 200,
    { timeout: API_TIMEOUT });
    await session.page.goto(`${runtime.appUrl}/app/users/${user.user.id}`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    await detailResponse;
    await waitForBodyText(session.page, expectedName);
    await waitForBodyText(session.page, user.user.email);
    if (new URL(session.page.url()).pathname !== `/app/users/${user.user.id}`) {
      throw new Error(`People detail route did not remain on the expected user for ${viewportName}.`);
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

async function main() {
  const runtime = requireRuntime();
  const admin = await identity(runtime, runtime.adminEmail, 'Admin');
  const user = await identity(runtime, runtime.userEmail, 'User');
  const originalUser = await api(runtime, admin, 'GET', `/api/users/${user.user.id}`, undefined, [200]);
  const updatedName = `${originalUser.displayName} browser coverage`;
  let jobId = null;

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

    await verifyPeopleLifecycle(runtime, admin, user, 'desktop', updatedName);
    await verifyPeopleLifecycle(runtime, admin, user, 'mobile', updatedName);

    const unique = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    const job = await api(runtime, admin, 'POST', '/api/jobs/', {
      customerSnapshot: {
        name: `Notification coverage ${unique}`,
        address: 'Testvej 3, 8000 Aarhus C',
        email: `notification-${unique}@example.test`,
        phone: '12345678',
        contactPerson: 'Browser coverage',
      },
      destinationAddress: 'Testvej 3',
      destinationZipCode: '8000',
      destinationCity: 'Aarhus C',
      jobType: 'KLS',
      assignedUserIds: [user.user.id],
    }, [200]);
    jobId = job?.id ?? null;
    const reportNumber = String(job?.reportNumber ?? '').trim();
    if (!jobId || !reportNumber) throw new Error('Notification fixture job did not return id/reportNumber.');

    await verifyNotificationLifecycle(runtime, user, 'desktop', reportNumber, true);
    await verifyNotificationLifecycle(runtime, user, 'mobile', reportNumber, false);

    console.log('Notification + people lifecycle browser coverage passed on desktop and mobile.');
  } finally {
    await api(runtime, admin, 'PATCH', `/api/users/${user.user.id}`, {
      displayName: originalUser.displayName ?? null,
      phone: originalUser.phone ?? null,
      role: originalUser.role ?? 'User',
    }, [200]).catch(() => {});
    if (jobId) {
      await api(runtime, admin, 'DELETE', `/api/jobs/${jobId}`, undefined, [200, 204, 404]).catch(() => {});
    }
  }
}

await main();
