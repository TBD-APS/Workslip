import { expect, test, type Page, type Route } from '@playwright/test';

const jobId = '33333333-3333-4333-8333-333333333333';
const userId = '11111111-1111-4111-8111-111111111111';
const organizationId = '22222222-2222-4222-8222-222222222222';

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
      taskDescription: 'Test af modal efter fortrydt afvisning',
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

async function configureAdminApp(page: Page): Promise<Diagnostics> {
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

  await page.addInitScript(() => {
    localStorage.setItem('authToken', 'wor-89-close-modal-playwright-token');
    localStorage.setItem('userEmail', 'wor-89-admin@example.test');
    localStorage.removeItem('workslip.reauthInFlight');
  });

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
        email: 'wor-89-admin@example.test',
        displayName: 'WOR-89 Admin',
        phone: null,
        role: 'Admin',
        roleDisplayName: 'Administrator',
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

    await fulfillJson(route, { error: 'unmocked_wor_89_close_modal_request', method, path }, 501);
  });

  return diagnostics;
}

async function undoRejection(page: Page, diagnostics: Diagnostics): Promise<void> {
  await page.goto(`/app/job/${jobId}`);
  await expect(page).toHaveURL(new RegExp(`/app/completed/${jobId}$`));

  await page.getByRole('button', { name: 'Fortryd afvisning' }).first().click();
  const confirmDialog = page.getByRole('dialog', { name: 'Fortryd afvisning' });
  await expect(confirmDialog).toBeVisible();
  await confirmDialog.getByRole('button', { name: 'Fortryd afvisning' }).click();

  const successDialog = page.getByRole('dialog', { name: 'Afvisningen er fortrudt' });
  await expect(successDialog).toBeVisible();
  await expect(successDialog.getByText('SAG-0089')).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`/app/completed/${jobId}$`));
  await expect.poll(() => diagnostics.statusRequests).toEqual([
    { status: 'InReview', rejectionNote: null },
  ]);
}

test.describe('@wor-89-close-modal navigation after undo rejection', () => {
  test('can return to the job list', async ({ page }) => {
    const diagnostics = await configureAdminApp(page);
    await undoRejection(page, diagnostics);

    await page.getByRole('dialog', { name: 'Afvisningen er fortrudt' })
      .getByRole('button', { name: 'Til sagslisten' })
      .click();

    await expect(page).toHaveURL(/\/app\/?$/);
    expect(diagnostics.issues).toEqual([]);
  });

  test('can stay on the completed job', async ({ page }) => {
    const diagnostics = await configureAdminApp(page);
    await undoRejection(page, diagnostics);

    const successDialog = page.getByRole('dialog', { name: 'Afvisningen er fortrudt' });
    await successDialog.getByRole('button', { name: 'Til sagen' }).click();

    await expect(successDialog).toBeHidden();
    await expect(page).toHaveURL(new RegExp(`/app/completed/${jobId}$`));
    await expect(page.getByRole('heading', { name: 'Sagsoverblik' })).toBeVisible();
    expect(diagnostics.issues).toEqual([]);
  });
});
