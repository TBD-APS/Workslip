export function createAdminScenarioHandlers(env, h) {
  const { APP_URL, API_TIMEOUT, UI_TIMEOUT, VIEWPORT_NAME, browser, devices, report } = env;
  const {
    createKlsDraftViaUi, completeAndSubmitKlsViaUi, approveJobViaUi, rejectJobViaUi, navigateToAttestation,
    waitForWizardStep, waitForApiResponse, unwrapCollection, createCustomerFixtureViaApi, createMinimalJobFixtureViaApi,
    sectionByHeading, createCustomerViaUi, assignedIds, readCustomerName, addWorksheetViaUi,
    assertStatus, readDestinationAddress, assertEqual, clickWizardStep, fillOverviewFields, waitForEnabled, extractInviteToken
  } = h;

async function invitationOnboardingFlow(session) {
  await session.step('admin sends unique invite through UI', async () => {
    await session.login('Admin');
    await session.page.goto(`${APP_URL}/app/settings`, { waitUntil: 'domcontentloaded' });
    await session.page.getByRole('heading', { name: 'Administrativt', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const input = session.page.getByPlaceholder('Skriv e-mail...');
    await input.fill(session.data.inviteEmail);
    const add = session.page.getByRole('button', { name: 'Tilføj e-mail', exact: true });
    if (await add.isVisible().catch(() => false)) await add.click();
    const inviteResponsePromise = session.page.waitForResponse((response) =>
      response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/auth/invite',
    { timeout: API_TIMEOUT });
    await session.page.getByRole('button', { name: 'Send invitation', exact: true }).click();
    const response = await inviteResponsePromise;
    if (!response.ok()) throw new Error(`Invite UI request returned HTTP ${response.status()}.`);
    const payload = await response.json();
    const result = payload?.results?.find((item) => item.email === session.data.inviteEmail) ?? payload?.results?.[0];
    const token = extractInviteToken(result?.inviteLink ?? result?.token);
    if (!result?.success || !token) throw new Error('Invite result did not contain a successful token.');
    session.inviteToken = token;
    report.retainedFixtures.push({ type: 'invite', identifier: `PLAYWRIGHT-${session.data.suffix}`, reason: 'No invite delete contract exists.' });
  });

  await session.step('invite acceptance UI reaches Microsoft handoff', async () => {
    const inviteContext = await browser.newContext({ ...devices[VIEWPORT_NAME], locale: 'da-DK' });
    const page = await inviteContext.newPage();
    try {
      await page.goto(`${APP_URL}/invite/${session.inviteToken}`, { waitUntil: 'domcontentloaded' });
      await page.getByRole('heading', { name: 'Du er inviteret til Workslip', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      const accept = page.getByRole('button', { name: 'Acceptér invitation', exact: true });
      if (await accept.isVisible().catch(() => false)) await accept.click();
      await page.locator('#displayName').fill(session.data.inviteeDisplayName);
      const phone = page.locator('#phone');
      if (await phone.isVisible().catch(() => false)) await phone.fill(session.data.phone);
      const continueButton = page.getByRole('button', { name: 'Fortsæt med Microsoft', exact: true });
      await continueButton.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await continueButton.click();
      await page.waitForURL((url) => url.hostname.includes('microsoftonline.com') || url.pathname.includes('/invite'), { timeout: API_TIMEOUT });
      session.scenarioReport.coverageNotes.push({
        area: 'Entra enrollment completion',
        status: page.url().includes('microsoftonline.com') ? 'external-handoff-verified' : 'handoff-not-observed',
        detail: 'Completion requires a real isolated Microsoft identity session; no credentials or storage state are committed or uploaded.',
      });
    } finally {
      await inviteContext.close();
    }
  });

  await session.step('invalid invite token fails recoverably', async () => {
    const invalidContext = await browser.newContext({ ...devices[VIEWPORT_NAME], locale: 'da-DK' });
    const page = await invalidContext.newPage();
    try {
      await page.goto(`${APP_URL}/invite/${crypto.randomUUID().replaceAll('-', '')}`, { waitUntil: 'domcontentloaded' });
      const body = (await page.locator('body').innerText()).toLowerCase();
      if (!/(ugyldig|udløbet|kunne ikke|ikke fundet|fejl)/.test(body)) throw new Error('Invalid invite token did not show a recoverable error message.');
    } finally { await invalidContext.close(); }
  });
}

async function assignmentLifecycleFlow(session) {
  await session.step('admin resolves the stable configured assignees', async () => {
    await session.login('Admin');
    session.referenceData = await session.getReferenceData();
    session.address = await session.getAddress();
    session.assignmentUsers = await session.getConfiguredUsers(['User', 'Admin']);
  }, { screenshot: false });
  const job = await createKlsDraftViaUi(session, {
    role: 'Admin',
    assignedUsers: session.assignmentUsers,
    duplicatePerAssignedUser: true,
  });

  await session.step('creation duplicates the job into independent assignments', async () => {
    if (job.createdJobIds.length !== session.assignmentUsers.length) {
      throw new Error(`Expected ${session.assignmentUsers.length} job copies; received ${job.createdJobIds.length}.`);
    }
    const copies = await Promise.all(job.createdJobIds.map((jobId) =>
      session.apiExpect('GET', `/api/jobs/${jobId}`, undefined, [200])));
    const copyByAssignedUserId = new Map();
    for (const copy of copies) {
      const ids = assignedIds(copy);
      if (ids.length !== 1) throw new Error(`Duplicated job ${copy.id} has ${ids.length} assignments instead of one.`);
      if (copyByAssignedUserId.has(ids[0])) throw new Error(`More than one duplicate was assigned to ${ids[0]}.`);
      copyByAssignedUserId.set(ids[0], copy);
    }
    for (const user of session.assignmentUsers) {
      if (!copyByAssignedUserId.has(user.id)) throw new Error(`No independent job copy was assigned to ${user.id}.`);
    }
    session.assignmentCopies = copyByAssignedUserId;
  });

  await session.step('each assignee completes only their own duplicate', async () => {
    const [user, admin] = session.assignmentUsers;
    const userJob = session.assignmentCopies.get(user.id);
    const adminJob = session.assignmentCopies.get(admin.id);
    if (!userJob?.id || !adminJob?.id) throw new Error('Independent assignment copies were not retained for the lifecycle test.');

    await session.logout();
    await session.login('User');
    const assignedJobs = unwrapCollection(await session.apiExpect('GET', '/api/jobs/my-assigned', undefined, [200]));
    if (!assignedJobs.some((item) => item.id === userJob.id)) throw new Error('User cannot see their duplicated assignment.');
    if (assignedJobs.some((item) => item.id === adminJob.id)) throw new Error('User can see another assignee’s duplicated assignment.');
    await completeAndSubmitKlsViaUi(session, userJob);
    const userSubmitted = await session.apiExpect('GET', `/api/jobs/${userJob.id}`, undefined, [200]);
    assertStatus(userSubmitted, ['InReview']);
    await session.logout();

    await session.login('Admin');
    const adminDraft = await session.apiExpect('GET', `/api/jobs/${adminJob.id}`, undefined, [200]);
    assertStatus(adminDraft, ['Draft', 'Kladde']);
    await completeAndSubmitKlsViaUi(session, adminJob);
    const adminSubmitted = await session.apiExpect('GET', `/api/jobs/${adminJob.id}`, undefined, [200]);
    assertStatus(adminSubmitted, ['InReview']);
  });

  await session.step('admin approves each independent duplicate', async () => {
    for (const assignedUser of session.assignmentUsers) {
      const copy = session.assignmentCopies.get(assignedUser.id);
      if (!copy?.id) throw new Error('Missing duplicate job before approval.');
      await approveJobViaUi(session, copy.id);
      const approved = await session.apiExpect('GET', `/api/jobs/${copy.id}`, undefined, [200]);
      assertStatus(approved, ['Approved', 'Godkendt']);
    }
  });
}

async function customerLifecycleFlow(session) {
  await session.step('admin login and third-party address discovery', async () => {
    await session.login('Admin');
    session.address = await session.getAddress();
  }, { screenshot: false });

  const customer = await createCustomerViaUi(session);
  await session.step('customer can be searched, favorited, and edited', async () => {
    await session.page.goto(`${APP_URL}/app/customers`, { waitUntil: 'domcontentloaded' });
    const search = session.page.getByPlaceholder('Søg kunder...');
    await search.fill(customer.name);
    const card = session.page.locator('button.job-card, button.top-customer-card').filter({ hasText: customer.name }).first();
    await card.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const favorite = card.getByRole('button', { name: /Tilføj til top|Fjern fra top/ }).first();
    if (await favorite.isVisible().catch(() => false)) {
      const favoriteResponse = waitForApiResponse(session.page, 'PATCH', `/api/customers/${customer.id}/top`, [200, 204]);
      await favorite.click();
      await favoriteResponse;
    }
    await card.click();
    await session.page.waitForURL((url) => url.pathname.includes(`/app/customers/${customer.id}`), { timeout: UI_TIMEOUT });
    const actions = session.page.getByRole('button', { name: 'Flere handlinger for kunde', exact: true });
    await actions.click();
    await session.page.getByRole('menuitem', { name: 'Rediger', exact: true }).click();
    const nameInput = session.page.locator('#edit-customer-name, #create-customer-name').first();
    await nameInput.fill(session.data.updatedCustomerName);
    const saveResponse = waitForApiResponse(session.page, 'PUT', `/api/customers/${customer.id}`, [200]);
    await session.page.getByRole('button', { name: 'Gem', exact: true }).click();
    await saveResponse;
  });

  await session.step('job keeps customer snapshot after customer update and deletion', async () => {
    const updated = await session.apiExpect('GET', `/api/customers/${customer.id}`, undefined, [200]);
    const job = await createMinimalJobFixtureViaApi(session, updated);
    const snapshotBefore = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    await session.page.goto(`${APP_URL}/app/customers/${customer.id}`, { waitUntil: 'domcontentloaded' });
    await session.page.getByRole('button', { name: 'Flere handlinger for kunde', exact: true }).click();
    await session.page.getByRole('menuitem', { name: 'Slet', exact: true }).click();
    const deleteResponse = waitForApiResponse(session.page, 'DELETE', `/api/customers/${customer.id}`, [200, 204]);
    const dialog = session.page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Slet', exact: true }).click();
    await deleteResponse;
    session.fixtures.customers = session.fixtures.customers.filter((id) => id !== customer.id);
    const jobAfter = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    if (readCustomerName(jobAfter) !== readCustomerName(snapshotBefore)) throw new Error('Deleting the customer changed the job customer snapshot.');
  });
}

async function worksheetIntegrityFlow(session) {
  await session.step('admin login and dynamic fixture discovery', async () => {
    await session.login('Admin');
    session.address = await session.getAddress();
    session.referenceData = await session.getReferenceData();
    session.worksheetUser = session.auth.user;
  }, { screenshot: false });
  const job = await createKlsDraftViaUi(session, { role: 'Admin' });

  await session.step('add worksheet with Danish decimal comma', async () => {
    await session.page.goto(`${APP_URL}/app/job/${job.id}`, { waitUntil: 'domcontentloaded' });
    await waitForWizardStep(session.page, 'Sagsdetaljer');
    await clickWizardStep(session.page, 'Timesedler');
    await addWorksheetViaUi(session, session.worksheetUser, '1,5');
    const persisted = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    const worksheet = persisted.worksheets?.find((item) => item.userId === session.worksheetUser.id);
    if (!worksheet || Number(worksheet.hoursWorked) !== 1.5) throw new Error('Danish decimal worksheet value 1,5 was not persisted as 1.5.');
    session.worksheetId = worksheet.id;
  });

  await session.step('edit and delete worksheet without duplicates', async () => {
    await session.page.getByRole('button', { name: 'Åbn handlinger for timeseddel', exact: true }).first().click();
    await session.page.getByRole('menuitem', { name: 'Rediger', exact: true }).click();
    const hours = session.page.getByLabel('Timer', { exact: true });
    await hours.fill('2,25');
    const updateResponse = waitForApiResponse(session.page, 'POST', `/api/worksheets/jobs/${job.id}`, [200]);
    await session.page.getByRole('button', { name: 'Gem', exact: true }).click();
    await updateResponse;
    let persisted = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    const matches = persisted.worksheets.filter((item) => item.userId === session.worksheetUser.id && Number(item.hoursWorked) === 2.25);
    if (matches.length !== 1) throw new Error(`Expected one updated worksheet; found ${matches.length}.`);

    await session.page.getByRole('button', { name: 'Åbn handlinger for timeseddel', exact: true }).first().click();
    await session.page.getByRole('menuitem', { name: 'Slet', exact: true }).click();
    const dialog = session.page.getByRole('dialog', { name: 'Slet timeseddel' });
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const deleteResponse = waitForApiResponse(session.page, 'DELETE', `/api/worksheets/${session.worksheetId}/jobs/${job.id}`, [200]);
    await dialog.getByRole('button', { name: 'Slet', exact: true }).click();
    await deleteResponse;
    persisted = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    if (persisted.worksheets.some((item) => item.id === session.worksheetId)) throw new Error('Deleted worksheet still exists.');
  });
}

async function diverseLifecycleFlow(session) {
  await session.step('admin login and runtime data discovery', async () => {
    await session.login('Admin');
    session.address = await session.getAddress();
    session.worksheetUser = session.auth.user;
  }, { screenshot: false });

  const job = await session.step('create diverse job through simple UI', async () => {
    await session.page.goto(`${APP_URL}/app/job/simple/new`, { waitUntil: 'domcontentloaded' });
    await session.page.getByRole('heading', { name: 'Simpelt job', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await fillOverviewFields(session, { customerName: session.data.customerName, address: session.address });
    await addWorksheetViaUi(session, session.worksheetUser, '1');
    const responsePromise = session.page.waitForResponse((response) =>
      response.request().method() === 'POST' && ['/api/jobs', '/api/jobs/'].includes(new URL(response.url()).pathname),
    { timeout: API_TIMEOUT });
    await waitForEnabled(session.page.getByRole('button', { name: 'Opret job', exact: true }), 'Opret job');
    await session.page.getByRole('button', { name: 'Opret job', exact: true }).click();
    const response = await responsePromise;
    if (!response.ok()) throw new Error(`Diverse job creation returned HTTP ${response.status()}.`);
    const created = await response.json();
    session.fixtures.jobs.push(created.id);
    session.scenarioReport.generatedFixtures.push({ type: 'job', id: created.id, source: 'UI + runtime API data' });
    await session.page.getByRole('heading', { name: 'Jobbet er oprettet', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    return created;
  });

  await session.step('diverse job follows review and approval lifecycle', async () => {
    const persisted = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    if (String(persisted.jobType ?? '').toLowerCase() !== 'diverse') throw new Error('Simple job was not persisted as Diverse.');
    assertStatus(persisted, ['InReview']);
    await approveJobViaUi(session, job.id);
    const approved = await session.apiExpect('GET', `/api/jobs/${job.id}`, undefined, [200]);
    assertStatus(approved, ['Approved', 'Godkendt']);
  });
}


  return {
    'invitation-onboarding': invitationOnboardingFlow,
    'assignment-lifecycle': assignmentLifecycleFlow,
    'customer-lifecycle': customerLifecycleFlow,
    'worksheet-integrity': worksheetIntegrityFlow,
    'diverse-lifecycle': diverseLifecycleFlow,
  };
}
