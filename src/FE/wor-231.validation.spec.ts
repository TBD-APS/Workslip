import { expect, test, devices, type Page } from '@playwright/test';

const baseUrl = 'http://127.0.0.1:4173';

const users = {
  admin: {
    id: '11111111-1111-1111-1111-111111111111',
    organizationId: '22222222-2222-2222-2222-222222222222',
    email: 'admin@example.test',
    displayName: 'Validation Admin',
    phone: '',
    role: 'Admin',
    roleDisplayName: 'Administrator',
    hoursThisWeek: null,
    hoursThisMonth: null,
    hoursBiweekly: null,
  },
  user: {
    id: '33333333-3333-3333-3333-333333333333',
    organizationId: '22222222-2222-2222-2222-222222222222',
    email: 'user@example.test',
    displayName: 'Validation User',
    phone: '',
    role: 'User',
    roleDisplayName: 'Bruger',
    hoursThisWeek: null,
    hoursThisMonth: null,
    hoursBiweekly: null,
  },
} as const;

const jobs = [
  {
    id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    reportNumber: '1001',
    status: 'Draft',
    softDeleted: false,
    taskDescription: 'Aktiv valideringssag',
    jobType: 'Diverse',
    customer: { name: 'Aktiv kunde', address: 'Aktivvej 1' },
    destinationAddress: 'Aktivvej 1',
    installationTypes: [],
    totalHours: 1,
    assignedUsers: [{ id: users.user.id, displayName: users.user.displayName }],
    reportDate: '2026-07-30T08:00:00Z',
    updatedAt: '2026-07-30T09:00:00Z',
    isSeen: true,
    isNewRejection: false,
  },
  {
    id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    reportNumber: '1002',
    status: 'Rejected',
    softDeleted: false,
    taskDescription: 'Afvist valideringssag',
    jobType: 'Diverse',
    customer: { name: 'Afvist kunde', address: 'Afvistvej 2' },
    destinationAddress: 'Afvistvej 2',
    installationTypes: [],
    totalHours: 2,
    assignedUsers: [{ id: users.user.id, displayName: users.user.displayName }],
    reportDate: '2026-07-30T08:00:00Z',
    updatedAt: '2026-07-30T10:00:00Z',
    isSeen: true,
    isNewRejection: true,
  },
  {
    id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    reportNumber: '1003',
    status: 'InReview',
    softDeleted: false,
    taskDescription: 'Gennemsyn valideringssag',
    jobType: 'Diverse',
    customer: { name: 'Gennemsyn kunde', address: 'Gennemsynsvej 3' },
    destinationAddress: 'Gennemsynsvej 3',
    installationTypes: [],
    totalHours: 3,
    assignedUsers: [{ id: users.user.id, displayName: users.user.displayName }],
    reportDate: '2026-07-30T08:00:00Z',
    updatedAt: '2026-07-30T11:00:00Z',
    isSeen: true,
    isNewRejection: false,
  },
  {
    id: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
    reportNumber: '1004',
    status: 'Approved',
    softDeleted: false,
    taskDescription: 'Godkendt valideringssag',
    jobType: 'Diverse',
    customer: { name: 'Godkendt kunde', address: 'Godkendtvej 4' },
    destinationAddress: 'Godkendtvej 4',
    installationTypes: [],
    totalHours: 4,
    assignedUsers: [{ id: users.user.id, displayName: users.user.displayName }],
    reportDate: '2026-07-30T08:00:00Z',
    updatedAt: '2026-07-30T12:00:00Z',
    isSeen: true,
    isNewRejection: false,
  },
];

function requestedStatuses(url: URL): string[] {
  return [...url.searchParams.entries()]
    .filter(([key]) => key.startsWith('status'))
    .map(([, value]) => value)
    .sort();
}

async function validateCombinedFilter(
  page: Page,
  account: (typeof users)[keyof typeof users],
  savedStatus: 'Draft' | 'Rejected',
) {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedApiRequests: string[] = [];
  const statusRequests: string[][] = [];

  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('requestfailed', (request) => {
    if (request.url().includes('/api/')) {
      failedApiRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`);
    }
  });

  await page.addInitScript(({ role, email, initialStatus }) => {
    const encode = (value: object) => btoa(JSON.stringify(value))
      .replace(/=/g, '')
      .replace(/\+/g, '-')
      .replace(/\//g, '_');
    const token = `${encode({ alg: 'none', typ: 'JWT' })}.${encode({
      role,
      exp: Math.floor(Date.now() / 1000) + 3600,
    })}.validation-signature`;

    localStorage.setItem('authToken', token);
    localStorage.setItem('userEmail', email);
    sessionStorage.setItem('statusFilter:lastActive', 'mine-jobs');
    sessionStorage.setItem('statusFilter:mine-jobs', JSON.stringify([initialStatus]));

    try {
      delete (window as Window & { PushManager?: unknown }).PushManager;
    } catch {
      // Web Push registration is outside this validation.
    }
  }, { role: account.role, email: account.email, initialStatus: savedStatus });

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.toLowerCase();

    if (path === '/api/auth/me') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(account) });
      return;
    }

    if (path === '/api/jobs') {
      const statuses = requestedStatuses(url);
      statusRequests.push(statuses);
      const items = statuses.length === 0
        ? jobs
        : jobs.filter((job) => statuses.includes(job.status));

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items, totalCount: items.length }),
      });
      return;
    }

    if (path === '/api/notifications') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
      return;
    }

    await route.fulfill({
      status: request.method() === 'GET' ? 200 : 204,
      contentType: 'application/json',
      body: request.method() === 'GET' ? '{}' : '',
    });
  });

  await page.goto(`${baseUrl}/app`);
  await expect(page.getByRole('heading', { name: 'Opgaver' })).toBeVisible();

  const combinedButton = page.getByRole('button', { name: 'Aktive og afviste', exact: true });
  await expect(combinedButton).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: 'Afvist', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Til gennemsyn', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await expect(page.getByRole('button', { name: 'Godkendt', exact: true })).toHaveAttribute('aria-pressed', 'false');

  await expect(page.getByText('Aktiv kunde')).toBeVisible();
  await expect(page.getByText('Afvist kunde')).toBeVisible();
  await expect(page.getByText('Gennemsyn kunde')).toHaveCount(0);

  await expect.poll(() => statusRequests.some((statuses) =>
    statuses.length === 2 && statuses.includes('Draft') && statuses.includes('Rejected'),
  )).toBe(true);

  const persisted = await page.evaluate(() => JSON.parse(sessionStorage.getItem('statusFilter:mine-jobs') ?? '[]'));
  expect(persisted.sort()).toEqual(['Draft', 'Rejected']);

  await page.getByRole('button', { name: 'Til gennemsyn', exact: true }).click();
  await expect(page.getByText('Gennemsyn kunde')).toBeVisible();

  await combinedButton.click();
  await expect(combinedButton).toHaveAttribute('aria-pressed', 'false');
  await expect(page.getByText('Gennemsyn kunde')).toBeVisible();
  await expect(page.getByText('Aktiv kunde')).toHaveCount(0);
  await expect(page.getByText('Afvist kunde')).toHaveCount(0);

  await expect.poll(() => statusRequests.some((statuses) =>
    statuses.length === 1 && statuses[0] === 'InReview',
  )).toBe(true);

  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(failedApiRequests).toEqual([]);
}

test.describe('Admin desktop', () => {
  test.use({ viewport: { width: 1280, height: 900 } });

  test('shows active and rejected jobs in one filter', async ({ page }) => {
    await validateCombinedFilter(page, users.admin, 'Draft');
  });
});

test.describe('User mobile', () => {
  test.use(devices['Pixel 7']);

  test('shows assigned active and rejected jobs in one filter', async ({ page }) => {
    await validateCombinedFilter(page, users.user, 'Rejected');
  });
});