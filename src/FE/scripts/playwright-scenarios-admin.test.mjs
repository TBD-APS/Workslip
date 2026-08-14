import assert from 'node:assert/strict';
import test from 'node:test';
import { createAdminScenarioHandlers } from './playwright-scenarios-admin.mjs';

test('assignment lifecycle completes and approves one independent duplicate per configured assignee', async () => {
  const user = { id: 'user-id', displayName: 'Configured user', role: 'User' };
  const admin = { id: 'admin-id', displayName: 'Configured admin', role: 'Admin' };
  const jobs = new Map([
    ['job-user', { id: 'job-user', assignedUsers: [user], status: 'Draft' }],
    ['job-admin', { id: 'job-admin', assignedUsers: [admin], status: 'Draft' }],
  ]);
  const completed = [];
  const approved = [];
  let activeRole = null;

  const handlers = createAdminScenarioHandlers(
    { APP_URL: 'https://app.example.test' },
    {
      createKlsDraftViaUi: async () => ({
        id: 'job-user',
        createdJobIds: ['job-user', 'job-admin'],
      }),
      completeAndSubmitKlsViaUi: async (_session, job) => {
        const persisted = jobs.get(job.id);
        assert.equal(persisted.assignedUsers[0].role, activeRole);
        persisted.status = 'InReview';
        completed.push(job.id);
      },
      approveJobViaUi: async (_session, jobId) => {
        assert.equal(activeRole, 'Admin');
        assert.equal(jobs.get(jobId).status, 'InReview');
        jobs.get(jobId).status = 'Approved';
        approved.push(jobId);
      },
      assignedIds: (job) => job.assignedUsers.map((assignedUser) => assignedUser.id),
      unwrapCollection: (value) => value,
      assertStatus: (job, statuses) => assert.ok(statuses.includes(job.status)),
    },
  );

  const session = {
    auth: { user: null },
    step: async (_label, action) => action(),
    login: async (role) => {
      activeRole = role;
      session.auth.user = role === 'User' ? user : admin;
    },
    logout: async () => {
      activeRole = null;
      session.auth.user = null;
    },
    getReferenceData: async () => ({}),
    getAddress: async () => ({}),
    getConfiguredUsers: async (roles) => {
      assert.deepEqual(roles, ['User', 'Admin']);
      return [user, admin];
    },
    apiExpect: async (method, path) => {
      assert.equal(method, 'GET');
      if (path === '/api/jobs/my-assigned') {
        assert.equal(activeRole, 'User');
        return [jobs.get('job-user')];
      }
      const job = jobs.get(path.slice('/api/jobs/'.length));
      assert.ok(job, `Unexpected job path ${path}`);
      return job;
    },
  };

  await handlers['assignment-lifecycle'](session);

  assert.deepEqual(completed, ['job-user', 'job-admin']);
  assert.deepEqual(approved, ['job-user', 'job-admin']);
  assert.equal(jobs.get('job-user').status, 'Approved');
  assert.equal(jobs.get('job-admin').status, 'Approved');
});
