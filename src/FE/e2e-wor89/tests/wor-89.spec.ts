import { expect, test, type Page, type Route } from '@playwright/test';

const jobId = '33333333-3333-4333-8333-333333333333';
const userId = '11111111-1111-4111-8111-111111111111';
const organizationId = '22222222-2222-4222-8222-222222222222';

type UserRole = 'Admin' | 'User';

type Diagnostics = {
  issues: string[];
  statusRequests: Array<{ status?: string; rejectionNote?: string | null }>;
};

function jobResponse(status: 'Rejected' | 'InReview') {
  return {
    id: jobId,
    organizationId,
    reportNumber: '0089',
    status,
    customerId: null,
    customerSnapshot: {
      name: 'WOR-89 kunde',
      email: null,
      phone: null,
      address: 'Testvej 89',
      contactPerson: null,
    },
    destinationAddress: 'Testvej 89',
    destinationZipCode: '8000',
    destinationCity: 'Aarhus C',
    work: {
      workKind: null,
      installationTypes: [],
      closureFlags: [],
      remarks: null,
    },
    observations: {
      taskDescription: 'Test af fortryd afvisning',
      customerObservations: null,
      technicalObservations: null,
    },
    links: [],
    assignedUsers: [],
    worksheets: [],
    totalHours: 0,
    totalOutlay: 0,
    softDeleted: false,
    jobType: 'Diverse',
    rejectionNote: status === 'Rejected' ? 'Mangler dokumentation' : null,
  };
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

async function configureApp(page: Page, role: UserRole): Promise<Diagnostics> {
  const diagnostics: Diagnostics = { issues: [], statusRequests: [] };
  let currentStatus: 'Rejected' | 'InReview' = 'Rejected';

  page.on('pageerror', (error) => diagnostics.issues.push(`Page error: ${error.message}`));
  page.on('console', (message) => {
    if (message.type() === 'error') diagnostics.issues.push(`Console error: ${message.text()}`);
  });
  page.on('requestfailed', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/')) {
      diagnostics.issues.push(`Failed API request: ${request.method()} ${request.url()}`);
    }
  });
  page.on('response', (response) => {
    if (new URL(response.url()).pathname.startsWith('/api/') && response.status() >= 400) {
      diagnostics.issues.push(`API response error: ${response.status()} ${response.url()}`);
    }
  });

  await page.addInitScript(({ email }) => {
    localStorage.setItem('authToken', 'wor-89-playwright-token');
    localStorage.setItem('userEmail', email);
    localStorage.removeItem('workslip.reauthInFlight');
  }, { email: `wor-89-${role.toLowerCase()}@example.test` });

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (!url.pathname.startsWith('/api/')) {
      await route.continue();
      return;
    }

    const path = url.pathname.replace(/\/+$/, '');
    const method = request.method();

    if (method === 'GET' && path === '/api/auth/me') {
      await fulfillJson(route, {
        id: userId,
        organizationId,
        email: `wor-89-${role.toLowerCase()}@example.test`,
        displayName: `WOR-89 ${role}`,
        phone: null,
        role,
        roleDisplayName: role === 'Admin' ? 'Administrator' : 'Medarbejder',
        hoursThisWeek: 0,
        hoursThisMonth: 0,
        hoursBiweekly: 0,
      });
      return;
    }

    if (method === 'GET' && path === `/api/jobs/${jobId}`) {
      await fulfillJson(route, jobResponse(currentStatus));
      return;
    }

    if (method === 'GET' && path === '/api/jobs') {
      await fulfillJson(route, { items: [], totalCount: 0 });
      return;
    }

    if (method === 'GET' && path === '/api/users') {
      await fulfillJson(route, { users: [], total: 0 });
      return;
    }

    if (method === 'GET' && path === '/api/reference-data') {
      await fulfillJson(route, {
        installationTypes: [],
        workKinds: [],
        closureFlags: [],
      });
      return;
    }

    if (method === 'GET' && path === '/api/notifications') {
      await fulfillJson(route, { items: [], totalCount: 0 });
      return;
    }

    if (method === 'POST' && path === '/api/push-subscriptions') {
      await route.fulfill({ status: 204, body: '' });
      return;
    }

    if (method === 'POST' && path === `/api/jobs/${jobId}/seen`) {
      await route.fulfill({ status: 204, body: '' });
      return;
    }

    if (method === 'POST' && path === `/api/jobs/${jobId}/status`) {
      const payload = request.postDataJSON() as { status?: string; rejectionNote?: string | null };
      diagnostics.statusRequests.push(payload);
      currentStatus = payload.status === 'InReview' ? 'InReview' : currentStatus;
      await fulfillJson(route, jobResponse(currentStatus));
      return;
    }

    await fulfillJson(route, { error: 'unmocked_wor_89_request', method, path }, 501);
  });

  return diagnostics;
}

for (const role of ['Admin', 'User'] as const) {
  test(`${role} rejected-job routing`, async ({ page }) => {
    const diagnostics = await configureApp(page, role);

    await page.goto(`/app/job/${jobId}`);

    if (role === 'Admin') {
      await expect(page).toHaveURL(new RegExp(`/app/completed/${jobId}$`));
      const undoButton = page.getByRole('button', { name: 'Fortryd afvisning' }).first();
      await expect(undoButton).toBeVisible();
      await undoButton.click();

      const dialog = page.getByRole('dialog', { name: 'Fortryd afvisning' });
      await expect(dialog).toBeVisible();
      await dialog.getByRole('button', { name: 'Fortryd afvisning' }).click();

      await expect.poll(() => diagnostics.statusRequests).toEqual([
        { status: 'InReview', rejectionNote: null },
      ]);
      await expect(page).toHaveURL(/\/app\/?$/);
    } else {
      await expect(page).toHaveURL(new RegExp(`/app/job/${jobId}$`));
      await expect(page.getByRole('button', { name: 'Fortryd afvisning' })).toHaveCount(0);
    }

    expect(diagnostics.issues).toEqual([]);
  });
}
