import assert from 'node:assert/strict';
import test from 'node:test';
import {
  authenticationHeaders,
  buildCheckpoint,
  endpointFrom,
  publishCheckpoint,
} from './publish-control-center-checkpoint.mjs';

const SHA = 'a'.repeat(40);
const NOW = new Date('2026-08-29T14:00:00.000Z');

function workflowRun(overrides = {}) {
  return {
    id: 4401,
    run_attempt: 1,
    updated_at: '2026-08-29T13:59:00.000Z',
    path: '.github/workflows/frontend-validation.yml',
    head_sha: SHA,
    html_url: 'https://github.com/rasm105k/Workslip-v2.0/actions/runs/4401',
    pull_requests: [{ number: 985 }],
    ...overrides,
  };
}

function checkpoint(action, overrides = {}) {
  return buildCheckpoint({
    action,
    workflowRun: workflowRun(overrides),
    repository: 'rasm105k/Workslip-v2.0',
    now: NOW,
  });
}

test('maps a retry start to a sanitized active checkpoint correlated to its pull request', () => {
  const result = checkpoint('in_progress', { run_attempt: 2, head_branch: 'customer-name-should-not-leave-github' });

  assert.equal(result.state, 'Active');
  assert.equal(result.id, 'workslip-ci-4401-2-active');
  assert.equal(result.correlationId, 'github:rasm105k/Workslip-v2.0:ci:pr-985');
  assert.equal(result.pullRequestReference, '#985');
  assert.equal(result.branch, undefined);
  assert.equal('pipeline' in result, false);
  assert.equal(result.summary, 'GitHub Actions CI retry is running.');
});

test('maps an actionable failed conclusion to a triage checkpoint with only allowlisted metadata', () => {
  const result = checkpoint('completed', { conclusion: 'failure' });

  assert.equal(result.state, 'Failed');
  assert.equal(result.kind, 'Checkpoint');
  assert.equal(result.summary, 'GitHub Actions CI failed.');
  assert.equal(result.reason, 'The CI workflow concluded with failure.');
  assert.equal(result.evidenceReference, 'https://github.com/rasm105k/Workslip-v2.0/actions/runs/4401');
  assert.equal(result.commitSha, SHA);
  assert.equal('head_commit' in result, false);
});

test('maps successful CI to a verified completion checkpoint on the same correlation', () => {
  const result = checkpoint('completed', { conclusion: 'success', run_attempt: 2 });

  assert.equal(result.state, 'Completed');
  assert.equal(result.id, 'workslip-ci-4401-2-completed');
  assert.equal(result.impact, 'The linked GitHub Actions CI run completed successfully.');
  assert.equal(result.correlationId, 'github:rasm105k/Workslip-v2.0:ci:pr-985');
});

test('does not misrepresent cancelled, skipped, or neutral CI as an incident outcome', () => {
  for (const conclusion of ['cancelled', 'skipped', 'neutral']) {
    assert.equal(checkpoint('completed', { conclusion }), null);
  }
});

test('uses the trusted default branch as the non-PR correlation scope', () => {
  const result = checkpoint('completed', {
    conclusion: 'failure',
    pull_requests: [],
    head_branch: 'main',
  });

  assert.equal(result.correlationId, 'github:rasm105k/Workslip-v2.0:ci:branch-main');
  assert.equal(result.pullRequestReference, undefined);
});

test('keeps backend deployment incidents separate from CI incidents', () => {
  const result = checkpoint('completed', {
    conclusion: 'failure',
    path: '.github/workflows/backend-production-deploy.yml',
    pull_requests: [],
    head_branch: 'main',
  });

  assert.equal(result.agentId, 'workslip-delivery');
  assert.equal(result.summary, 'GitHub Actions backend deployment failed.');
  assert.equal(result.correlationId, 'github:rasm105k/Workslip-v2.0:backend-deploy:branch-main');
});

test('fails closed when the workflow is not an allowlisted delivery pipeline', () => {
  assert.throws(
    () => checkpoint('completed', { conclusion: 'failure', path: '.github/workflows/untrusted.yml' }),
    /allowlisted delivery pipeline/,
  );
});

test('requires an HTTPS endpoint and never adds partial Cloudflare credentials', () => {
  assert.equal(endpointFrom('https://app.mrsoftware.dk/api/activity/checkpoints'), 'https://app.mrsoftware.dk/api/activity/checkpoints');
  assert.throws(() => endpointFrom('http://app.mrsoftware.dk/api/activity/checkpoints'), /absolute HTTPS/);
  assert.equal(authenticationHeaders({}), null);
  assert.throws(() => authenticationHeaders({ cloudflareClientId: 'id-only' }), /ID\/secret pair/);

  const headers = authenticationHeaders({
    activityToken: 'activity-token',
    cloudflareClientId: 'cf-id',
    cloudflareClientSecret: 'cf-secret',
  });
  assert.equal(headers['X-MR-SAASY-ACTIVITY-TOKEN'], 'activity-token');
  assert.equal(headers['CF-Access-Client-Id'], 'cf-id');
  assert.equal(headers['CF-Access-Client-Secret'], 'cf-secret');
});

test('retries transient delivery failures without exposing a response body', async () => {
  const calls = [];
  await publishCheckpoint({
    endpoint: 'https://app.mrsoftware.dk/api/activity/checkpoints',
    headers: { 'X-MR-SAASY-ACTIVITY-TOKEN': 'token' },
    checkpoint: checkpoint('completed', { conclusion: 'failure' }),
    fetchImpl: async (...args) => {
      calls.push(args);
      return { ok: calls.length === 2, status: calls.length === 2 ? 201 : 503 };
    },
    sleep: async () => {},
  });

  assert.equal(calls.length, 2);
  assert.equal(calls[0][1].method, 'POST');
  assert.match(calls[0][1].body, /GitHub Actions CI failed/);
});

test('does not retry an authentication or validation rejection', async () => {
  let calls = 0;
  await assert.rejects(
    publishCheckpoint({
      endpoint: 'https://app.mrsoftware.dk/api/activity/checkpoints',
      headers: { 'X-MR-SAASY-ACTIVITY-TOKEN': 'token' },
      checkpoint: checkpoint('completed', { conclusion: 'failure' }),
      fetchImpl: async () => {
        calls += 1;
        return { ok: false, status: 401 };
      },
      sleep: async () => {},
    }),
    /HTTP 401/,
  );
  assert.equal(calls, 1);
});
