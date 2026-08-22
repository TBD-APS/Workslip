import { randomUUID } from 'node:crypto';
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

function assignedIds(job) {
  return (job?.assignedUsers ?? job?.assignedUserIds ?? [])
    .map((item) => (typeof item === 'string' ? item : item?.id))
    .filter(Boolean);
}

function unwrapCollection(payload) {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.items)) return payload.items;
  if (Array.isArray(payload?.value)) return payload.value;
  return [];
}

function jobCreateBody({ unique, assignedUserIds, duplicatePerAssignedUser, linkedJobIds, taskDescription }) {
  return {
    customerId: null,
    customerSnapshot: {
      name: `Duplicate coverage ${unique}`,
      email: `duplicate-${unique}@example.test`,
      phone: '20112233',
      address: 'Testvej 1, 8000 Aarhus C',
      contactPerson: 'Browser coverage',
    },
    createCustomerFromSnapshot: false,
    destinationAddress: 'Testvej 1',
    destinationZipCode: '8000',
    destinationCity: 'Aarhus C',
    jobType: 'KLS',
    assignedUserIds,
    duplicatePerAssignedUser,
    linkedJobIds,
    work: null,
    observations: {
      reportDate: null,
      taskDescription,
      customerObservations: null,
      technicalObservations: null,
    },
  };
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
  const actor = { token: tokenPayload.token, email };
  const user = await api(runtime, actor, 'GET', '/api/auth/me');
  if (!user?.id || String(user.role ?? '').toLowerCase() !== expectedRole.toLowerCase()) {
    throw new Error(`Could not resolve ${expectedRole} identity.`);
  }
  return { ...actor, user };
}

async function api(runtime, actor, method, pathname, body, expectedStatuses = [200], idempotencyKey) {
  const headers = {
    Accept: 'application/json',
    Authorization: `Bearer ${actor.token}`,
  };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;
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

async function openJob(session, runtime, jobId) {
  const detail = session.page.waitForResponse((response) => {
    const pathname = new URL(response.url()).pathname;
    return response.request().method() === 'GET'
      && (pathname === `/api/jobs/${jobId}` || pathname === `/api/jobs/${jobId}/`)
      && [200, 403, 404].includes(response.status());
  }, { timeout: API_TIMEOUT });
  await session.page.goto(`${runtime.appUrl}/app/job/${jobId}`, {
    waitUntil: 'domcontentloaded',
    timeout: UI_TIMEOUT,
  });
  return detail;
}

async function main() {
  const runtime = requireRuntime();
  const admin = await identity(runtime, runtime.adminEmail, 'Admin');
  const user = await identity(runtime, runtime.userEmail, 'User');
  const unique = `${Date.now()}-${randomUUID().slice(0, 8)}`;
  const createdIds = [];

  try {
    const source = await api(runtime, admin, 'POST', '/api/jobs', jobCreateBody({
      unique: `${unique}-source`,
      assignedUserIds: [admin.user.id],
      duplicatePerAssignedUser: false,
      linkedJobIds: [],
      taskDescription: `Original sag ${unique}`,
    }), [200, 201]);
    if (!source?.id) throw new Error('Source job did not return an id.');
    createdIds.push(source.id);

    const idempotencyKey = `playwright-duplicate-${unique}`;
    const duplicated = await api(
      runtime,
      admin,
      'POST',
      '/api/jobs',
      jobCreateBody({
        unique: `${unique}-copies`,
        assignedUserIds: [user.user.id, admin.user.id],
        duplicatePerAssignedUser: true,
        linkedJobIds: [source.id],
        taskDescription: `Kopi pr. medarbejder ${unique}`,
      }),
      [200, 201],
      idempotencyKey,
    );

    const copyIds = duplicated?.createdJobIds?.length ? duplicated.createdJobIds : [duplicated?.id].filter(Boolean);
    createdIds.push(...copyIds);
    if (copyIds.length !== 2) {
      throw new Error(`Expected one independent copy per assignee; received ${copyIds.length}.`);
    }

    const retry = await api(
      runtime,
      admin,
      'POST',
      '/api/jobs',
      jobCreateBody({
        unique: `${unique}-copies`,
        assignedUserIds: [user.user.id, admin.user.id],
        duplicatePerAssignedUser: true,
        linkedJobIds: [source.id],
        taskDescription: `Kopi pr. medarbejder ${unique}`,
      }),
      [200, 201],
      idempotencyKey,
    );
    const retryIds = retry?.createdJobIds?.length ? retry.createdJobIds : [retry?.id].filter(Boolean);
    const extra = retryIds.filter((id) => !copyIds.includes(id) && id !== source.id);
    if (extra.length > 0) {
      createdIds.push(...extra);
      throw new Error(`Retry with the same idempotency key created extra jobs: ${extra.join(', ')}.`);
    }

    const copies = await Promise.all(copyIds.map((id) => api(runtime, admin, 'GET', `/api/jobs/${id}`)));
    const byAssignee = new Map();
    for (const copy of copies) {
      if (copy.id === source.id) throw new Error('A generated copy reused the original job id.');
      const ids = assignedIds(copy);
      if (ids.length !== 1) throw new Error(`Copy ${copy.id} has ${ids.length} assignees instead of one.`);
      if (byAssignee.has(ids[0])) throw new Error(`More than one copy was assigned to ${ids[0]}.`);
      byAssignee.set(ids[0], copy);
    }
    if (!byAssignee.has(user.user.id) || !byAssignee.has(admin.user.id)) {
      throw new Error('Copy-per-employee did not produce one job for User and one for Admin.');
    }

    const userCopy = byAssignee.get(user.user.id);
    const adminCopy = byAssignee.get(admin.user.id);
    const userAssigned = unwrapCollection(await api(runtime, user, 'GET', '/api/jobs/my-assigned'));
    if (!userAssigned.some((item) => item.id === userCopy.id)) {
      throw new Error('User cannot see their own duplicated assignment.');
    }
    if (userAssigned.some((item) => item.id === adminCopy.id || item.id === source.id)) {
      throw new Error('User can see another assignee or the original job.');
    }

    const mutatedDescription = `User-only observation ${unique}`;
    await api(runtime, user, 'PATCH', `/api/jobs/${userCopy.id}`, {
      observations: {
        reportDate: null,
        taskDescription: mutatedDescription,
        customerObservations: null,
        technicalObservations: null,
      },
    }, [200]);

    const userAfter = await api(runtime, admin, 'GET', `/api/jobs/${userCopy.id}`);
    const adminAfter = await api(runtime, admin, 'GET', `/api/jobs/${adminCopy.id}`);
    const sourceAfter = await api(runtime, admin, 'GET', `/api/jobs/${source.id}`);
    if (String(userAfter?.observations?.taskDescription ?? '') !== mutatedDescription) {
      throw new Error('User copy observation did not persist independently.');
    }
    if (String(adminAfter?.observations?.taskDescription ?? '').includes('User-only observation')) {
      throw new Error('User observation leaked into the Admin copy.');
    }
    if (String(sourceAfter?.observations?.taskDescription ?? '').includes('User-only observation')) {
      throw new Error('User observation leaked into the original job.');
    }

    await api(runtime, user, 'GET', `/api/jobs/${adminCopy.id}`, undefined, [403, 404]);

    for (const viewportName of ['desktop', 'mobile']) {
      const own = await browserFor(viewportName);
      try {
        await authenticate(own.page, runtime, user);
        const ownResponse = await openJob(own, runtime, userCopy.id);
        if (ownResponse.status() !== 200) {
          throw new Error(`User could not open their own copy on ${viewportName} (HTTP ${ownResponse.status()}).`);
        }
        await own.page.locator('#account-menu-button').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        if (!new URL(own.page.url()).pathname.includes(`/app/job/${userCopy.id}`)) {
          throw new Error(`User was not kept on their own copy on ${viewportName}.`);
        }
        own.assertClean();
      } finally {
        await own.context.close();
        await own.browser.close();
      }

      const foreign = await browserFor(viewportName);
      try {
        await authenticate(foreign.page, runtime, user);
        const foreignResponse = await openJob(foreign, runtime, adminCopy.id);
        if (foreignResponse.status() === 200) {
          throw new Error(`User opened another assignee copy on ${viewportName}.`);
        }
        foreign.assertClean();
      } finally {
        await foreign.context.close();
        await foreign.browser.close();
      }
    }

    console.log('Duplicate-assignment lifecycle coverage passed on desktop and mobile.');
  } finally {
    for (const id of [...new Set(createdIds)]) {
      await api(runtime, admin, 'DELETE', `/api/jobs/${id}`, undefined, [200, 204, 404]).catch(() => {});
    }
  }
}

await main();
