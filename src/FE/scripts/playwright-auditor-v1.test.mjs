import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const scenarioUrl = new URL('./playwright-auditor-v1.mjs', import.meta.url).href;

function prepareFixture({ sharedOrganization = false, superadminRole = 'Superadmin', existingControl = false, controlRole = 'Auditor' } = {}) {
  const users = [
    { id: 'superadmin-id', organizationId: 'platform-id', email: 'superadmin@workslip.invalid', role: superadminRole },
    { id: 'auditor-id', organizationId: sharedOrganization ? 'target-id' : 'control-id', email: 'auditor@workslip.invalid', role: 'Auditor' },
    { id: 'admin-id', organizationId: 'target-id', email: 'admin@workslip.invalid', role: 'Admin' },
  ];
  const organizations = existingControl
    ? [{ id: 'new-control-id', name: 'Playwright WOR-814 control organization', cvr: '90000814' }]
    : [];
  if (existingControl) users.push({
    id: 'new-auditor-id', organizationId: 'new-control-id', email: 'auditor-v1@workslip-playwright.invalid', role: controlRole, userKind: 'InternalTest',
  });
  const program = `
    import assert from 'node:assert/strict';
    const users = ${JSON.stringify(users)};
    const organizations = ${JSON.stringify(organizations)};
    const changes = [];
    let assignment = null;
    const json = (body) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });
    globalThis.fetch = async (url, options = {}) => {
      const pathname = new URL(url).pathname;
      const body = options.body ? JSON.parse(options.body) : null;
      const actorEmail = options.headers?.Authorization?.replace('Bearer ', '');
      const actor = users.find((user) => user.email === actorEmail);
      if (pathname === '/api/dev/token') {
        const user = users.find((user) => user.email === body.email);
        assert.ok(user, 'Token must reference a known fixture user.');
        return json({ token: user.email, user: { ...user, userId: user.id } });
      }
      assert.ok(actor, 'API calls must use an issued fixture identity.');
      if (pathname === '/api/auth/me') return json(actor);
      if (pathname === '/api/auditor/organizations') throw new Error('fixture-ready');
      assert.equal(actor.role, 'Superadmin', 'Only Superadmin may prepare auditor assignments.');
      if (pathname === '/api/superadmin/users' && options.method === 'GET') return json({ users, total: users.length });
      if (pathname === '/api/organizations') {
        if (options.method === 'GET') return json(organizations);
        const organization = { id: 'new-control-id', name: body.name, cvr: body.cvr };
        const user = { id: 'new-auditor-id', organizationId: organization.id, email: body.adminEmail, role: 'Admin' };
        organizations.push(organization);
        users.push(user);
        changes.push('control-organization');
        return json({ organization, user });
      }
      if (pathname === '/api/superadmin/users/new-auditor-id' && options.method === 'PATCH') {
        const user = users.find((candidate) => candidate.id === 'new-auditor-id');
        assert.equal(body.role, 'Auditor');
        assert.equal(body.userKind, 'InternalTest');
        Object.assign(user, body);
        changes.push('control-auditor');
        return json(user);
      }
      if (pathname === '/api/auditor/admin/assignments') {
        const auditor = users.find((user) => user.id === body.auditorUserId);
        assert.equal(auditor.role, 'Auditor');
        assert.notEqual(auditor.organizationId, body.targetOrganizationId, 'Assignments require a distinct control organization.');
        assignment = body;
        return json({ id: 'assignment-id', ...body });
      }
      throw new Error('Unexpected fixture request: ' + options.method + ' ' + pathname);
    };
    try {
      await import(${JSON.stringify(scenarioUrl)});
      throw new Error('Scenario must stop before importing the browser.');
    } catch (error) {
      console.log(JSON.stringify({ message: error.message, assignment, changes, users }));
    }
  `;
  const child = spawnSync(process.execPath, ['--input-type=module', '--eval', program], {
    encoding: 'utf8',
    timeout: 10_000,
    env: {
      ...process.env,
      WORKSLIP_PLAYWRIGHT_API_URL: 'http://127.0.0.1:5262',
      WORKSLIP_PLAYWRIGHT_APP_URL: 'http://127.0.0.1:5270',
      WORKSLIP_PLAYWRIGHT_SUPERADMIN_EMAIL: users[0].email,
      WORKSLIP_PLAYWRIGHT_AUDITOR_EMAIL: users[1].email,
      WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL: users[2].email,
    },
  });
  assert.equal(child.status, 0, child.stderr);
  return JSON.parse(child.stdout.trim());
}

test('Auditor browser setup accepts the auth/me payload and keeps an existing cross-tenant identity', () => {
  const fixture = prepareFixture();
  assert.equal(fixture.message, 'fixture-ready');
  assert.equal(fixture.assignment.auditorUserId, 'auditor-id');
  assert.equal(fixture.assignment.targetOrganizationId, 'target-id');
  assert.deepEqual(fixture.changes, []);
});

test('Auditor browser setup creates a separate control tenant without moving the shared seed users', () => {
  const fixture = prepareFixture({ sharedOrganization: true });
  assert.equal(fixture.message, 'fixture-ready');
  assert.equal(fixture.assignment.auditorUserId, 'new-auditor-id');
  assert.equal(fixture.assignment.targetOrganizationId, 'target-id');
  assert.deepEqual(fixture.changes, ['control-organization', 'control-auditor']);
  assert.equal(fixture.users.find((user) => user.id === 'auditor-id').organizationId, 'target-id');
});

test('Auditor browser setup rejects a superadmin email that resolves to another role', () => {
  const fixture = prepareFixture({ superadminRole: 'Admin' });
  assert.match(fixture.message, /resolved to the wrong role/);
  assert.equal(fixture.assignment, null);
  assert.deepEqual(fixture.changes, []);
});

test('Auditor browser setup reuses its existing control identity without changing roles', () => {
  const fixture = prepareFixture({ sharedOrganization: true, existingControl: true });
  assert.equal(fixture.message, 'fixture-ready');
  assert.equal(fixture.assignment.auditorUserId, 'new-auditor-id');
  assert.deepEqual(fixture.changes, []);
});

test('Auditor browser setup refuses a reused control identity with the wrong role', () => {
  const fixture = prepareFixture({ sharedOrganization: true, existingControl: true, controlRole: 'Admin' });
  assert.match(fixture.message, /resolved to the wrong role/);
  assert.equal(fixture.assignment, null);
  assert.deepEqual(fixture.changes, []);
});
