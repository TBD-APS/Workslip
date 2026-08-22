import process from 'node:process';

const API_TIMEOUT = 30_000;
const UI_TIMEOUT = 25_000;
const MOBILE_DEVICE_NAME = 'iPhone 13';

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

function runtime() {
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

async function api(config, actor, method, pathname, body, expectedStatuses, extraHeaders = {}) {
  const headers = {
    Accept: 'application/json',
    Authorization: `Bearer ${actor.token}`,
    ...extraHeaders,
  };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${config.apiUrl}${pathname}`, {
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
    throw new Error(`${method} ${pathname} returned ${response.status}; expected ${expectedStatuses.join('/')}. Payload: ${JSON.stringify(payload)}`);
  }
  return payload;
}

async function identity(config, email, expectedRole) {
  const response = await fetch(`${config.apiUrl}/api/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email }),
    signal: AbortSignal.timeout(API_TIMEOUT),
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok || !payload?.token) {
    throw new Error(`Could not issue ${expectedRole} token (HTTP ${response.status}).`);
  }
  const actor = { token: payload.token, email };
  const user = await api(config, actor, 'GET', '/api/auth/me', undefined, [200]);
  if (!user?.id || String(user.role ?? '').toLowerCase() !== expectedRole.toLowerCase()) {
    throw new Error(`Could not resolve synthetic ${expectedRole} identity.`);
  }
  return { ...actor, user };
}

async function browserFor(config, viewportName) {
  const { chromium, devices } = await import('playwright');
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    ...(viewportName === 'desktop' ? { viewport: { width: 1280, height: 800 } } : devices[MOBILE_DEVICE_NAME]),
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
  });
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: config.appUrl });
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
        throw new Error(`Field interaction browser diagnostics failed: ${[...pageErrors, ...consoleErrors].join(' | ')}`);
      }
    },
  };
}

async function authenticate(page, config, actor) {
  await page.goto(`${config.appUrl}/login`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.evaluate(({ token, email }) => {
    window.localStorage.setItem('authToken', token);
    window.localStorage.setItem('userEmail', email);
  }, { token: actor.token, email: actor.email });
  await page.goto(`${config.appUrl}/app`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
  await page.locator('#account-menu-button').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
}

function currentLocation(page) {
  const url = new URL(page.url());
  return `${url.pathname}${url.search}${url.hash}`;
}

async function readClipboard(page, expected, label) {
  let actual = '';
  for (let attempt = 0; attempt < 20; attempt += 1) {
    actual = await page.evaluate(() => navigator.clipboard.readText()).catch(() => '');
    if (actual === expected) break;
    await page.waitForTimeout(50);
  }
  if (actual !== expected) {
    throw new Error(`${label} copied ${JSON.stringify(actual)}; expected ${JSON.stringify(expected)}.`);
  }
}

async function assertDirectClipboardCopy(page, selector, expected, label) {
  const target = page.locator(selector);
  await target.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const locationBefore = currentLocation(page);
  await target.click();
  await readClipboard(page, expected, label);
  if (currentLocation(page) !== locationBefore) {
    throw new Error(`${label} unexpectedly navigated away while copying.`);
  }
}

async function assertActionMenuCopy(
  page,
  triggerSelector,
  copySelector,
  expected,
  label,
  secondaryAction,
) {
  const trigger = page.locator(triggerSelector);
  await trigger.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const locationBefore = currentLocation(page);
  await trigger.click();

  if (secondaryAction) {
    const secondary = page.locator(secondaryAction.selector);
    await secondary.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const href = await secondary.getAttribute('href');
    if (href !== secondaryAction.href) {
      throw new Error(`${label} secondary action href was ${JSON.stringify(href)}; expected ${JSON.stringify(secondaryAction.href)}.`);
    }
  }

  const copy = page.locator(copySelector);
  await copy.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await copy.click();
  await readClipboard(page, expected, label);

  if (currentLocation(page) !== locationBefore) {
    throw new Error(`${label} unexpectedly navigated away while using its action menu.`);
  }
}

async function verifyCustomer(config, admin, customer, viewportName) {
  const session = await browserFor(config, viewportName);
  try {
    await authenticate(session.page, config, admin);
    await session.page.goto(`${config.appUrl}/app/customers`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    const search = session.page.locator('#customer-search-input');
    await search.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await search.fill(customer.name);
    const row = session.page.locator(`#customer-list-item-${customer.id}`);
    await row.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    await assertDirectClipboardCopy(
      session.page,
      `#customer-list-name-${customer.id}`,
      customer.name,
      `${viewportName} customer list name`,
    );
    await assertActionMenuCopy(
      session.page,
      `#customer-list-phone-${customer.id}`,
      `#customer-list-phone-${customer.id}-copy`,
      customer.phone,
      `${viewportName} customer list phone`,
      { selector: `#customer-list-phone-${customer.id}-call`, href: `tel:${customer.phone}` },
    );

    await session.page.goto(`${config.appUrl}/app/customers/${customer.id}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await session.page.locator('#customer-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await assertDirectClipboardCopy(session.page, '#customer-detail-name', customer.name, `${viewportName} customer detail name`);
    await assertActionMenuCopy(
      session.page,
      '#customer-detail-email',
      '#customer-detail-email-copy',
      customer.email,
      `${viewportName} customer detail email`,
      { selector: '#customer-detail-email-email', href: `mailto:${customer.email}` },
    );
    await assertActionMenuCopy(
      session.page,
      '#customer-detail-phone',
      '#customer-detail-phone-copy',
      customer.phone,
      `${viewportName} customer detail phone`,
      { selector: '#customer-detail-phone-call', href: `tel:${customer.phone}` },
    );
    await assertDirectClipboardCopy(
      session.page,
      '#customer-detail-address',
      customer.fullAddress,
      `${viewportName} customer detail address`,
    );

    const maps = session.page.locator('#customer-detail-address-actions-maps');
    await maps.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const mapsHref = await maps.getAttribute('href');
    if (!mapsHref || !mapsHref.includes(encodeURIComponent(customer.fullAddress))) {
      throw new Error(`${viewportName} customer detail lost the Google Maps address action.`);
    }

    session.assertClean();
  } finally {
    await session.context.close();
    await session.browser.close();
  }
}

async function verifyPeople(config, admin, person, viewportName) {
  const session = await browserFor(config, viewportName);
  try {
    await authenticate(session.page, config, admin);
    await session.page.goto(`${config.appUrl}/app/users`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await session.page.locator(`#user-list-name-${person.id}`).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await assertDirectClipboardCopy(
      session.page,
      `#user-list-name-${person.id}`,
      person.displayName,
      `${viewportName} people list name`,
    );
    await assertActionMenuCopy(
      session.page,
      `#user-list-email-${person.id}`,
      `#user-list-email-${person.id}-copy`,
      person.email,
      `${viewportName} people list email`,
      { selector: `#user-list-email-${person.id}-email`, href: `mailto:${person.email}` },
    );

    await session.page.goto(`${config.appUrl}/app/users/${person.id}`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await session.page.locator('#user-detail-page').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await assertDirectClipboardCopy(
      session.page,
      '#user-detail-name',
      person.displayName,
      `${viewportName} people detail name`,
    );
    await assertActionMenuCopy(
      session.page,
      '#user-detail-email',
      '#user-detail-email-copy',
      person.email,
      `${viewportName} people detail email`,
      { selector: '#user-detail-email-email', href: `mailto:${person.email}` },
    );
    await assertActionMenuCopy(
      session.page,
      '#user-detail-phone',
      '#user-detail-phone-copy',
      person.phone,
      `${viewportName} people detail phone`,
      { selector: '#user-detail-phone-call', href: `tel:${person.phone}` },
    );

    session.assertClean();
  } finally {
    await session.context.close();
    await session.browser.close();
  }
}

async function main() {
  const config = runtime();
  const admin = await identity(config, config.adminEmail, 'Admin');
  const user = await identity(config, config.userEmail, 'User');
  const originalUser = await api(config, admin, 'GET', `/api/users/${user.user.id}`, undefined, [200]);
  const unique = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  const customerName = `Copyability kunde ${unique}`;
  const customerPhone = '11223344';
  const customerEmail = `copy-${unique}@example.test`;
  const customerAddress = 'Kopivej 7';
  const customerZipCode = '8000';
  const customerCity = 'Aarhus C';
  const customerCountry = 'Danmark';
  const fullAddress = `${customerAddress}, ${customerZipCode} ${customerCity}, ${customerCountry}`;
  const testPhone = '87654321';
  let customerId = null;

  try {
    const createdCustomer = await api(config, admin, 'POST', '/api/customers/', {
      name: customerName,
      customerNumber: `COPY-${unique.slice(-8)}`,
      address: customerAddress,
      zipCode: customerZipCode,
      city: customerCity,
      country: customerCountry,
      email: customerEmail,
      contactPerson: 'Kopi Kontakt',
      phone: customerPhone,
    }, [200], { 'Idempotency-Key': `copy-customer-${unique}` });
    customerId = createdCustomer?.id ?? null;
    if (!customerId) throw new Error('Copyability customer fixture did not return an id.');

    await api(config, admin, 'PATCH', `/api/users/${user.user.id}`, {
      displayName: originalUser.displayName,
      phone: testPhone,
      role: originalUser.role ?? 'User',
    }, [200]);
    const person = await api(config, admin, 'GET', `/api/users/${user.user.id}`, undefined, [200]);
    if (person?.phone !== testPhone) throw new Error('Copyability people phone fixture did not persist.');

    const customer = {
      id: customerId,
      name: customerName,
      phone: customerPhone,
      email: customerEmail,
      fullAddress,
    };

    for (const viewportName of ['desktop', 'mobile']) {
      await verifyCustomer(config, admin, customer, viewportName);
      await verifyPeople(config, admin, person, viewportName);
    }

    console.log('Global field interaction evidence passed on desktop and mobile, including copy/call/e-mail actions.');
  } finally {
    await api(config, admin, 'PATCH', `/api/users/${user.user.id}`, {
      displayName: originalUser.displayName ?? null,
      phone: originalUser.phone ?? null,
      role: originalUser.role ?? 'User',
    }, [200]).catch(() => {});
    if (customerId) {
      await api(config, admin, 'DELETE', `/api/customers/${customerId}`, undefined, [200, 204, 404]).catch(() => {});
    }
  }
}

await main();
