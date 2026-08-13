export function createDomainHelpers(env, c) {
  const { APP_URL, API_TIMEOUT, UI_TIMEOUT, postman } = env;
  const {
    postmanBody, pickReferenceSelection, valueOf, candidates, unwrapCollection,
    fillIfVisible, waitForEnabled, waitForWizardStep, currentWizardStep, clickNext,
    clickByTextCandidates, checkRadioByCandidates, waitForApiResponse, escapeRegex,
    sectionByHeading
  } = c;

async function createKlsDraftViaUi(session, { role, assignedUsers = [], duplicatePerAssignedUser = false }) {
  return session.step('create KLS draft through UI', async () => {
    await session.page.goto(`${APP_URL}/app/job/new`, { waitUntil: 'domcontentloaded' });
    await session.page.getByRole('heading', { name: 'Ny sag', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await fillOverviewFields(session, { customerName: session.data.customerName, address: session.address });
    if (assignedUsers.length > 0) {
      const trigger = sectionByHeading(session.page, 'Tildelte medarbejdere').locator('button.multi-select-trigger');
      await waitForEnabled(trigger, 'assignment selector');
      await trigger.click();
      const selectedOptions = session.page.locator('[role="option"][aria-selected="true"]');
      while (await selectedOptions.count()) await selectedOptions.first().click();
      for (const assignedUser of assignedUsers) {
        await session.page.getByRole('option', { name: assignedUser.displayName, exact: true }).click();
      }
      await trigger.click();
      if (duplicatePerAssignedUser) {
        const duplicate = session.page.getByRole('checkbox', {
          name: /Opret en kopi af sagen til hver medarbejder/,
        });
        await duplicate.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        await duplicate.check();
      }
    }
    const responsePromise = session.page.waitForResponse((response) =>
      response.request().method() === 'POST' && ['/api/jobs', '/api/jobs/'].includes(new URL(response.url()).pathname),
    { timeout: API_TIMEOUT });
    const create = session.page.getByRole('button', { name: 'Opret sag', exact: true });
    await waitForEnabled(create, 'Opret sag');
    await create.click();
    const response = await responsePromise;
    if (!response.ok()) throw new Error(`KLS draft creation returned HTTP ${response.status()}.`);
    const created = await response.json();
    if (!created?.id) throw new Error('KLS draft response had no id.');
    const createdJobIds = created.createdJobIds?.length ? created.createdJobIds : [created.id];
    session.fixtures.jobs.push(...createdJobIds);
    for (const id of createdJobIds) {
      session.scenarioReport.generatedFixtures.push({ type: 'job', id, source: 'UI + runtime API/DAWA data' });
    }
    await session.page.getByRole('heading', { name: /sag(?:en|er) er oprettet/i }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    return { id: created.id, createdJobIds, reportNumber: created.reportNumber, customerName: session.data.customerName, role };
  });
}

async function fillOverviewFields(session, { customerName, address }) {
  const page = session.page;
  const destination = page.getByPlaceholder('Søg adresse...').first();
  if (await destination.isVisible().catch(() => false)) {
    await destination.fill(address.text);
    const suggestion = page.getByRole('option').filter({ hasText: address.text.split(',')[0] }).first();
    if (await suggestion.isVisible({ timeout: 5_000 }).catch(() => false)) await suggestion.click();
    else await destination.press('Tab');
  }

  const customerPicker = page.getByRole('button', { name: 'Vælg kunde...', exact: true });
  if (await customerPicker.isVisible().catch(() => false)) {
    await customerPicker.click();
    const createOption = page.getByRole('option', { name: /Opret ny kunde/ });
    if (await createOption.isVisible().catch(() => false)) await createOption.click();
  }

  await fillIfVisible(page.getByPlaceholder('Kundenavn', { exact: true }), customerName);
  await fillIfVisible(page.getByPlaceholder('Adresse', { exact: true }), address.text);
  await fillIfVisible(page.getByPlaceholder('Email', { exact: true }), session.data.customerEmail);
  await fillIfVisible(page.getByPlaceholder('Telefon', { exact: true }), session.data.phone);
  await fillIfVisible(page.getByPlaceholder('Kontaktperson', { exact: true }), session.data.contactPerson);
  await fillIfVisible(page.getByPlaceholder('Beskriv opgaven...'), session.data.taskDescription);
}

async function completeAndSubmitKlsViaUi(session, job) {
  await session.step('complete KLS wizard and submit', async () => {
    await session.page.getByRole('button', { name: 'Til sagslisten', exact: true }).click();
    await session.page.goto(`${APP_URL}/app/job/${job.id}`, { waitUntil: 'domcontentloaded' });
    await waitForWizardStep(session.page, 'Sagsdetaljer');
    const referenceData = session.referenceData ?? await session.getReferenceData();
    const selection = pickReferenceSelection(referenceData);

    await clickNext(session.page, 'Anlægstyper');
    await clickByTextCandidates(session.page.locator('button.choice-card.selection-card'), candidates(selection.installation), 'installation type');
    await checkRadioByCandidates(session.page, candidates(selection.workKind), 'work kind');
    const custom = session.page.getByPlaceholder('Skriv hvilken opgavetype der udføres');
    if (await custom.isVisible().catch(() => false)) await custom.fill(session.data.customWorkKind);

    await clickNext(session.page, 'Kontrolpunkter');
    const irrelevant = session.page.locator('button[title="Marker som ikke relevant"]');
    let guard = 0;
    while (await irrelevant.count()) {
      if (guard++ > 100) throw new Error('Control-point processing exceeded safety limit.');
      await irrelevant.first().click();
      await session.page.waitForTimeout(75);
    }

    await clickNext(session.page, 'Timesedler');
    const users = session.runtimeUsers?.length ? session.runtimeUsers : await ensureAssignableUsers(session, 1);
    await addWorksheetViaUi(session, users[0], '1');

    await clickNext(session.page, 'Afslutning');
    await clickByTextCandidates(session.page.locator('button'), candidates(selection.closureFlag), 'closure flag');
    await clickNext(session.page, 'Attestering');
    await session.page.getByRole('checkbox', { name: /Jeg bekræfter, at sagen er gennemgået/ }).check();
    const response = waitForApiResponse(session.page, 'POST', `/api/jobs/${job.id}/status`, [200]);
    await session.page.getByRole('button', { name: 'Attestér og indsend', exact: true }).click();
    await response;
    await session.page.getByRole('heading', { name: 'Sag sendt til kontoret', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  });
}

async function navigateToAttestation(session, referenceData) {
  const page = session.page;
  const labels = ['Anlægstyper', 'Kontrolpunkter', 'Timesedler', 'Afslutning', 'Attestering'];
  for (const label of labels) {
    const active = page.getByRole('button', { name: `${label} - aktuelt trin`, exact: true });
    if (await active.isVisible().catch(() => false)) continue;
    const stepButton = page.getByRole('button', { name: new RegExp(`^${escapeRegex(label)}`) });
    if (await stepButton.isVisible().catch(() => false) && !(await stepButton.isDisabled())) {
      await stepButton.click();
      await waitForWizardStep(page, label);
      continue;
    }
    const current = await currentWizardStep(page);
    if (current === 'Anlægstyper') {
      const selection = pickReferenceSelection(referenceData);
      const cards = page.locator('button.choice-card.selection-card');
      if (await cards.count()) await clickByTextCandidates(cards, candidates(selection.installation), 'installation type');
      await checkRadioByCandidates(page, candidates(selection.workKind), 'work kind');
    }
    if (current === 'Kontrolpunkter') {
      const buttons = page.locator('button[title="Marker som ikke relevant"]');
      while (await buttons.count()) await buttons.first().click();
    }
    if (current === 'Timesedler') {
      // Existing submitted/rejected job should already contain a worksheet.
    }
    if (current === 'Afslutning') {
      const selection = pickReferenceSelection(referenceData);
      await clickByTextCandidates(page.locator('button'), candidates(selection.closureFlag), 'closure flag');
    }
    await clickNext(page, labels[Math.min(labels.indexOf(current) + 1, labels.length - 1)]);
  }
}

async function approveJobViaUi(session, jobId) {
  await session.page.goto(`${APP_URL}/app/completed/${jobId}`, { waitUntil: 'domcontentloaded' });
  await session.page.getByRole('heading', { name: 'Sagsoverblik', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const approve = session.page.locator('button:visible').filter({ hasText: /^Godkend$/ }).last();
  await approve.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await approve.click();
  const dialog = session.page.getByRole('dialog', { name: 'Godkend sag' });
  await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const response = waitForApiResponse(session.page, 'POST', `/api/jobs/${jobId}/status`, [200]);
  await dialog.getByRole('button', { name: 'Godkend', exact: true }).click();
  await response;
}

async function rejectJobViaUi(session, jobId, rejectionNote = 'Mangler dokumentation for udført arbejde.') {
  await session.page.goto(`${APP_URL}/app/completed/${jobId}`, { waitUntil: 'domcontentloaded' });
  await session.page.getByRole('heading', { name: 'Sagsoverblik', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await session.page.locator('button:visible').filter({ hasText: /^Afvis$/ }).last().click();
  const dialog = session.page.getByRole('dialog', { name: 'Afvis sag' });
  await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await dialog.locator('#rejection-note').fill(rejectionNote);
  const response = waitForApiResponse(session.page, 'POST', `/api/jobs/${jobId}/status`, [200]);
  await dialog.getByRole('button', { name: 'Afvis', exact: true }).click();
  await response;
}

async function createCustomerViaUi(session) {
  return session.step('create customer through UI', async () => {
    await session.page.goto(`${APP_URL}/app/customers/new`, { waitUntil: 'domcontentloaded' });
    await session.page.getByRole('heading', { name: 'Opret kunde', exact: true }).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await session.page.locator('#create-customer-name').fill(session.data.customerName);
    await session.page.locator('#create-customer-address').fill(session.address.text);
    await session.page.locator('#create-customer-email').fill(session.data.customerEmail);
    await session.page.locator('#create-customer-contact').fill(session.data.contactPerson);
    await session.page.locator('#create-customer-phone').fill(session.data.phone);
    const responsePromise = session.page.waitForResponse((response) => response.request().method() === 'POST' && ['/api/customers', '/api/customers/'].includes(new URL(response.url()).pathname), { timeout: API_TIMEOUT });
    await session.page.getByRole('button', { name: 'Opret', exact: true }).click();
    const response = await responsePromise;
    if (!response.ok()) throw new Error(`Customer creation returned HTTP ${response.status()}.`);
    const payload = await response.json();
    const id = payload.id ?? payload.customerId;
    if (!id) throw new Error('Customer creation response did not include id.');
    session.fixtures.customers.push(id);
    session.scenarioReport.generatedFixtures.push({ type: 'customer', id, source: 'UI + DAWA data' });
    return { ...payload, id, name: session.data.customerName };
  });
}

async function createCustomerFixtureViaApi(session) {
  const payload = postmanBody(postman, '/api/customers (create)');
  const body = {
    ...payload,
    name: session.data.customerName,
    address: session.address.text,
    email: session.data.customerEmail,
    contactPerson: session.data.contactPerson,
    phone: session.data.phone,
  };
  const created = await session.apiExpect('POST', '/api/customers/', body, [200, 201]);
  const id = created.id ?? created.customerId;
  if (!id) throw new Error('Customer fixture response did not include id.');
  session.fixtures.customers.push(id);
  return { ...created, ...body, id };
}

async function createMinimalJobFixtureViaApi(session, customer) {
  const referenceData = await session.getReferenceData();
  const selection = pickReferenceSelection(referenceData);
  const body = {
    customerId: customer.id,
    customerSnapshot: {
      name: customer.name,
      address: customer.address ?? session.address?.text ?? null,
      email: customer.email ?? session.data.customerEmail,
      contactPerson: customer.contactPerson ?? session.data.contactPerson,
      phone: customer.phone ?? session.data.phone,
    },
    reportNumber: session.data.reportNumber,
    destinationAddress: session.address?.street ?? null,
    destinationZipCode: session.address?.zipCode ?? null,
    destinationCity: session.address?.city ?? null,
    work: {
      installationTypes: [{
        id: selection.installation.id,
        categories: selection.installation.categories?.slice(0, 1).map((category) => ({
          id: category.id,
          controlPoints: category.controlPoints?.slice(0, 1).map((controlPoint) => ({ id: controlPoint.id })) ?? [],
        })) ?? [],
      }],
      workKind: valueOf(selection.workKind),
      closureFlags: [valueOf(selection.closureFlag)],
    },
    observations: { taskDescription: session.data.taskDescription },
  };
  const created = await session.apiExpect('POST', '/api/jobs/', body, [200, 201]);
  session.fixtures.jobs.push(created.id);
  return created;
}

async function ensureAssignableUsers(session, count) {
  const bodyTemplate = postmanBody(postman, '/api/users');
  const requiredRole = String(bodyTemplate.role);
  if (!requiredRole) throw new Error('Postman /api/users request must define a role.');
  let users = unwrapCollection(await session.apiExpect('GET', '/api/users/', undefined, [200]))
    .filter((user) => user.id && user.email && user.displayName && String(user.role).toLowerCase() === requiredRole.toLowerCase());
  while (users.length < count) {
    const index = users.length + 1;
    const body = {
      ...bodyTemplate,
      email: session.data.userEmail(index),
      displayName: `${session.data.userDisplayName} ${index}`,
      phone: session.data.phone,
      role: requiredRole,
    };
    const created = await session.apiExpect('POST', '/api/users/', body, [200, 201]);
    session.fixtures.users.push(created.id);
    users.push(created);
  }
  return users.slice(0, count);
}

async function addWorksheetViaUi(session, user, hours) {
  const page = session.page;
  const add = page.getByRole('button', { name: 'Tilføj timeseddel', exact: true });
  await add.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await add.click();
  const form = page.locator('.worksheet-form');
  const trigger = form.locator('button.multi-select-trigger');
  if (await trigger.isVisible().catch(() => false)) {
    await trigger.click();
    const option = page.getByRole('option', { name: user.displayName, exact: true });
    if (await option.isVisible().catch(() => false)) await option.click();
    await trigger.click();
  }
  await page.getByLabel('Timer', { exact: true }).fill(hours);
  await page.getByRole('button', { name: 'Tilføj', exact: true }).click();
  await form.waitFor({ state: 'hidden', timeout: API_TIMEOUT });
}

  return {
    createKlsDraftViaUi,
    fillOverviewFields,
    completeAndSubmitKlsViaUi,
    navigateToAttestation,
    approveJobViaUi,
    rejectJobViaUi,
    createCustomerViaUi,
    createCustomerFixtureViaApi,
    createMinimalJobFixtureViaApi,
    ensureAssignableUsers,
    addWorksheetViaUi
  };
}
