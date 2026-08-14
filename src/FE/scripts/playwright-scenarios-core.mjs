export function createCoreScenarioHandlers(env, h) {
  const { APP_URL, API_TIMEOUT, UI_TIMEOUT, VIEWPORT_NAME, browser, devices, report } = env;
  const {
    createKlsDraftViaUi, completeAndSubmitKlsViaUi, approveJobViaUi, rejectJobViaUi, navigateToAttestation,
    waitForWizardStep, waitForApiResponse, unwrapCollection, createCustomerFixtureViaApi, createMinimalJobFixtureViaApi,
    sectionByHeading, createCustomerViaUi, assignedIds, readCustomerName, addWorksheetViaUi,
    assertStatus, readDestinationAddress, assertEqual, clickWizardStep, fillOverviewFields, waitForEnabled, extractInviteToken
  } = h;

async function publicSmoke(session) {
  await session.step('public home responds', async () => {
    const response = await session.page.goto(APP_URL, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    if (!response?.ok()) throw new Error(`App returned HTTP ${response?.status() ?? 'no response'}.`);
    await session.page.locator('body').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  });
}

async function authSessionFlow(session) {
  for (const requestedRole of ['User', 'Auditor', 'Admin', 'Superadmin']) {
    await session.step(`${requestedRole} login and session persistence`, async () => {
      await session.login(requestedRole);
      const roleFromApi = String(session.auth.user.role);
      await session.page.reload({ waitUntil: 'domcontentloaded' });
      await session.page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: UI_TIMEOUT });
      const meAfterReload = await session.apiExpect('GET', '/api/auth/me', undefined, [200]);
      if (String(meAfterReload.role) !== roleFromApi) throw new Error(`${requestedRole} role changed after reload.`);
      await session.page.goBack({ waitUntil: 'domcontentloaded' }).catch(() => null);
      await session.page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded' });
      await session.page.waitForURL((url) => url.pathname.startsWith('/app'), { timeout: UI_TIMEOUT });
      await session.logout();
      await session.page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded' });
      await session.page.waitForURL((url) => url.pathname === '/login', { timeout: UI_TIMEOUT });
    }, { screenshot: requestedRole === 'Superadmin' });
  }

  await session.step('invalid stored token cannot expose protected app', async () => {
    await session.login('User');
    await session.page.evaluate(() => localStorage.setItem('authToken', 'invalid.token.value'));
    await session.page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded' });
    await session.page.waitForTimeout(2_000);
    for (const entry of session.scenarioReport.failedApiResponses) {
      if (entry.status === 401) entry.expected = true;
    }
    const protectedShellVisible = await session.page.locator('.app-shell').isVisible().catch(() => false);
    if (protectedShellVisible && session.page.url().includes('/app')) {
      throw new Error('Protected app shell remained visible after corrupting the stored token.');
    }
  });
}

async function klsLifecycleFlow(session) {
  await session.step('admin login and runtime data discovery', async () => {
    await session.login('Admin');
    session.referenceData = await session.getReferenceData();
    session.address = await session.getAddress();
  }, { screenshot: false });

  const job = await createKlsDraftViaUi(session, { role: 'Admin' });
  await completeAndSubmitKlsViaUi(session, job);
  await approveJobViaUi(session, job.id);
  await session.step('approved KLS data persisted', async () => {
    const persisted = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    assertStatus(persisted, ['Approved', 'Godkendt']);
    assertEqual(readCustomerName(persisted), job.customerName, 'Persisted customer name');
    if (!readDestinationAddress(persisted)) throw new Error('Persisted job has no destination address.');
    if (!Array.isArray(persisted.worksheets) || persisted.worksheets.length === 0) throw new Error('Persisted job has no worksheet.');
  });
}

async function rejectionLoopFlow(session) {
  await session.step('user login and runtime data discovery', async () => {
    await session.login('User');
    session.referenceData = await session.getReferenceData();
    session.address = await session.getAddress();
  }, { screenshot: false });
  const job = await createKlsDraftViaUi(session, { role: 'User' });
  await completeAndSubmitKlsViaUi(session, job);
  await session.logout();

  const rejectionNote = 'Mangler dokumentation for udført arbejde.';
  await session.step('admin rejects submitted job', async () => {
    await session.login('Admin');
    await rejectJobViaUi(session, job.id, rejectionNote);
    const persisted = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    assertStatus(persisted, ['Rejected', 'Afvist']);
    assertEqual(persisted.rejectionNote, rejectionNote, 'Persisted rejection note');
  });
  await session.logout();

  await session.step('user corrects and resubmits rejected job', async () => {
    await session.login('User');
    await session.page.goto(`${APP_URL}/app/job/${job.id}`, { waitUntil: 'domcontentloaded' });
    await waitForWizardStep(session.page, 'Sagsdetaljer');
    const technical = session.page.getByPlaceholder('Skriv en kommentar til sagen...');
    const correctionSave = waitForApiResponse(session.page, 'PATCH', `/api/jobs/${job.id}`, [200]);
    await technical.fill(session.data.correctedObservation);
    await correctionSave;
    await navigateToAttestation(session, session.referenceData ?? await session.getReferenceData());
    const confirmation = session.page.getByRole('checkbox', { name: /Jeg bekræfter, at sagen er gennemgået/ });
    await confirmation.check();
    const submitted = waitForApiResponse(session.page, 'POST', `/api/jobs/${job.id}/status`, [200]);
    await session.page.getByRole('button', { name: 'Attestér og indsend', exact: true }).click();
    await submitted;
  });
  await session.logout();

  await session.step('admin approves corrected job', async () => {
    await session.login('Admin');
    await approveJobViaUi(session, job.id);
    const history = await session.apiExpect('GET', `/api/jobs/${job.id}/history`, undefined, [200]);
    const historyText = JSON.stringify(history);
    for (const expected of ['Rejected', 'InReview', 'Approved']) {
      if (!historyText.toLowerCase().includes(expected.toLowerCase())) throw new Error(`Job history does not contain ${expected}.`);
    }
  });
}

async function draftRecoveryFlow(session) {
  await session.step('user login and create draft', async () => {
    await session.login('User');
    session.referenceData = await session.getReferenceData();
    session.address = await session.getAddress();
  }, { screenshot: false });
  const job = await createKlsDraftViaUi(session, { role: 'User' });

  await session.step('autosave survives reload and browser navigation', async () => {
    await session.page.goto(`${APP_URL}/app/job/${job.id}`, { waitUntil: 'domcontentloaded' });
    await waitForWizardStep(session.page, 'Sagsdetaljer');
    const task = session.page.getByPlaceholder('Beskriv opgaven...');
    const initialSave = waitForApiResponse(session.page, 'PATCH', `/api/jobs/${job.id}`, [200]);
    await task.fill(session.data.taskDescription);
    await initialSave;
    await session.page.reload({ waitUntil: 'domcontentloaded' });
    await waitForWizardStep(session.page, 'Sagsdetaljer');
    if ((await task.inputValue()) !== session.data.taskDescription) throw new Error('Autosaved task description was lost after reload.');
    await session.page.goto(`${APP_URL}/app`, { waitUntil: 'domcontentloaded' });
    await session.page.goBack({ waitUntil: 'domcontentloaded' });
    await waitForWizardStep(session.page, 'Sagsdetaljer');
  });

  await session.step('failed autosave can be retried without stale overwrite', async () => {
    let blocked = false;
    await session.page.route(`**/api/jobs/${job.id}`, async (route) => {
      if (!blocked && route.request().method() === 'PATCH') {
        blocked = true;
        await route.abort('failed');
        return;
      }
      await route.continue();
    });
    const customerInfo = session.page.getByPlaceholder('Notér oplysninger til kunden...');
    await customerInfo.fill(session.data.failedSaveText);
    await session.page.waitForTimeout(2_500);
    for (const entry of session.scenarioReport.failedApiResponses) {
      if (entry.method === 'PATCH' && entry.url?.includes(`/api/jobs/${job.id}`)) entry.expected = true;
    }
    await session.page.unroute(`**/api/jobs/${job.id}`);
    const retrySave = waitForApiResponse(session.page, 'PATCH', `/api/jobs/${job.id}`, [200]);
    await customerInfo.fill(session.data.retriedSaveText);
    await retrySave;
    const persisted = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    const observations = persisted.observations ?? {};
    if (observations.customerObservations !== session.data.retriedSaveText) throw new Error('Retry did not persist the latest customer observation.');
    const pwaState = await session.page.evaluate(async () => {
      if (!('serviceWorker' in navigator)) return { serviceWorkerSupported: false, controlled: false, updateRequested: false };
      const registration = await navigator.serviceWorker.getRegistration();
      if (!registration) return { serviceWorkerSupported: true, controlled: Boolean(navigator.serviceWorker.controller), updateRequested: false };
      await registration.update();
      return {
        serviceWorkerSupported: true,
        controlled: Boolean(navigator.serviceWorker.controller),
        updateRequested: true,
        waiting: Boolean(registration.waiting),
        installing: Boolean(registration.installing),
      };
    });
    session.scenarioReport.coverageNotes.push({
      area: 'PWA update during active work',
      status: pwaState.updateRequested ? 'registration-update-requested' : 'service-worker-not-active',
      detail: pwaState,
    });
  });
}

async function roleTenantIsolationFlow(session) {
  const roleResults = [];
  for (const role of ['User', 'Auditor', 'Admin', 'Superadmin']) {
    await session.step(`${role} identity and route boundary`, async () => {
      const me = await session.login(role);
      roleResults.push({ requested: role, actual: me.role, organizationId: me.organizationId });
      const target = role === 'Auditor' ? '/app/auditor' : role === 'Superadmin' ? '/superadmin' : '/app';
      await session.page.goto(`${APP_URL}${target}`, { waitUntil: 'domcontentloaded' });
      if (role === 'Auditor') await session.page.waitForURL((url) => url.pathname.startsWith('/app/auditor'), { timeout: UI_TIMEOUT });
      await session.logout();
    }, { screenshot: false });
  }
  session.scenarioReport.roles = roleResults;

  await session.step('cross-tenant identifiers are rejected', async () => {
    await session.login('Admin');
    session.address = await session.getAddress();
    const primaryCustomer = await createCustomerFixtureViaApi(session);
    const primaryJob = await createMinimalJobFixtureViaApi(session, primaryCustomer);
    await session.logout();

    await session.login('Superadmin');
    const secondary = session.data.secondaryOrganization;
    const organization = await session.apiExpect('POST', '/api/organizations/', secondary, [200, 201]);
    report.retainedFixtures.push({ type: 'organization', identifier: organization?.organization?.id ?? secondary.cvr, reason: 'No organization delete contract exists.' });
    const secondaryAdmin = await session.authenticateEmail(secondary.adminEmail);
    const secondaryToken = session.auth.token;
    if (!secondaryToken || String(secondaryAdmin.role).toLowerCase() !== 'admin') {
      throw new Error('Secondary organization admin could not authenticate through OTC.');
    }

    for (const resourcePath of [`/api/jobs/${primaryJob.id}`, `/api/customers/${primaryCustomer.id}`]) {
      const result = await session.api('GET', resourcePath, undefined, { token: secondaryToken });
      if (![403, 404].includes(result.response.status)) throw new Error(`Cross-tenant GET ${resourcePath} returned ${result.response.status}; expected 403/404.`);
    }
    const ownJobs = unwrapCollection((await session.api('GET', '/api/jobs/?limit=100&offset=0', undefined, { token: secondaryToken })).payload);
    if (ownJobs.some((item) => item.id === primaryJob.id)) throw new Error('Secondary tenant job list leaked a primary tenant job.');

    await session.logout();
    await session.login('Admin');
  });
}

  return {
    'public-smoke': publicSmoke,
    'auth-session': authSessionFlow,
    'kls-lifecycle': klsLifecycleFlow,
    'rejection-loop': rejectionLoopFlow,
    'draft-recovery': draftRecoveryFlow,
    'role-tenant-isolation': roleTenantIsolationFlow,
  };
}
