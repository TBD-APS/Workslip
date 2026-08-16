import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import {
  boundedRateLimitWaitMs,
  chooseRun,
  githubRateLimitRetryMs,
  ignoredBuildStepExitCode,
  parseGitHubRepository,
  productionGateMode,
  validateGate,
} from './vercel-production-eligibility.mjs';

const SHA = 'a'.repeat(40);
const OTHER_SHA = 'b'.repeat(40);

function run(overrides = {}) {
  return {
    id: 101,
    head_sha: SHA,
    head_branch: 'main',
    event: 'push',
    status: 'completed',
    conclusion: 'success',
    ...overrides,
  };
}

function gate(overrides = {}) {
  return {
    id: 202,
    name: 'CI Gate',
    status: 'completed',
    conclusion: 'success',
    ...overrides,
  };
}

test('Vercel Git policy uses a globstar catch-all and enables only main', () => {
  const config = JSON.parse(readFileSync(new URL('../vercel.json', import.meta.url), 'utf8'));

  assert.deepEqual(config.git?.deploymentEnabled, {
    '**': false,
    main: true,
  });
});

test('Vercel ignored build step continues eligible deploys and ignores blocked deploys', () => {
  assert.equal(ignoredBuildStepExitCode({ shouldDeploy: true }), 1);
  assert.equal(ignoredBuildStepExitCode({ shouldDeploy: false }), 0);
});

test('preview and development builds skip only the production eligibility gate', () => {
  assert.deepEqual(
    productionGateMode({ vercelEnv: 'preview', commitRef: 'release-4.9.0' }),
    { enforce: false, environment: 'preview' },
  );
  assert.deepEqual(
    productionGateMode({ vercelEnv: 'development', commitRef: 'feature/test' }),
    { enforce: false, environment: 'development' },
  );
});

test('production and manual gate execution remain fail-closed', () => {
  assert.deepEqual(
    productionGateMode({ vercelEnv: 'production', commitRef: 'main' }),
    { enforce: true, environment: 'production' },
  );
  assert.deepEqual(
    productionGateMode({}),
    { enforce: true, environment: 'manual' },
  );
  assert.throws(
    () => productionGateMode({ vercelEnv: 'production', commitRef: 'release-4.9.0' }),
    /only release main/,
  );
  assert.throws(
    () => productionGateMode({ vercelEnv: 'staging', commitRef: 'main' }),
    /Unsupported VERCEL_ENV/,
  );
});

test('parses GitHub HTTPS and SSH remotes for metadata fallback', () => {
  assert.equal(parseGitHubRepository('https://github.com/rasm105k/Workslip-v2.0.git'), 'rasm105k/Workslip-v2.0');
  assert.equal(parseGitHubRepository('git@github.com:rasm105k/Workslip-v2.0.git'), 'rasm105k/Workslip-v2.0');
  assert.throws(() => parseGitHubRepository('https://example.com/owner/repo.git'), /not a GitHub repository/);
});

test('accepts exact current main with one green CI Gate', () => {
  const evidence = validateGate({ expectedSha: SHA, mainSha: SHA, run: run(), jobs: [gate()] });
  assert.equal(evidence.sha, SHA);
});

test('rejects a stale SHA', () => {
  assert.throws(() => validateGate({ expectedSha: SHA, mainSha: OTHER_SHA, run: run(), jobs: [gate()] }), /stale/);
});

test('rejects non-success workflow conclusions', () => {
  for (const conclusion of ['failure', 'cancelled', 'timed_out', 'action_required', 'neutral', 'skipped', null]) {
    assert.throws(
      () => validateGate({ expectedSha: SHA, mainSha: SHA, run: run({ conclusion }), jobs: [gate()] }),
      /not green/,
    );
  }
});

test('rejects missing, duplicate or red CI Gate', () => {
  assert.throws(() => validateGate({ expectedSha: SHA, mainSha: SHA, run: run(), jobs: [] }), /exactly one CI Gate/);
  assert.throws(
    () => validateGate({ expectedSha: SHA, mainSha: SHA, run: run(), jobs: [gate(), gate({ id: 203 })] }),
    /exactly one CI Gate/,
  );
  assert.throws(
    () => validateGate({ expectedSha: SHA, mainSha: SHA, run: run(), jobs: [gate({ conclusion: 'cancelled' })] }),
    /CI Gate is not green/,
  );
});

test('run discovery is fail-closed', () => {
  assert.equal(chooseRun([run()], SHA).state, 'success');
  assert.equal(chooseRun([run({ status: 'in_progress', conclusion: null })], SHA).state, 'pending');
  assert.equal(chooseRun([run({ conclusion: 'failure' })], SHA).state, 'failed');
  assert.equal(chooseRun([], SHA).state, 'missing');
});

test('classifies GitHub primary rate-limit 403 as retryable', () => {
  const retryMs = githubRateLimitRetryMs({
    status: 403,
    headers: {},
    body: '{"message":"API rate limit exceeded for 3.234.236.5."}',
    nowMs: 1_700_000_000_000,
  });

  assert.equal(retryMs, 120_000);
});

test('uses GitHub rate-limit reset when remaining is zero', () => {
  const nowMs = 1_700_000_000_000;
  const retryMs = githubRateLimitRetryMs({
    status: 403,
    headers: {
      'x-ratelimit-remaining': '0',
      'x-ratelimit-reset': String((nowMs / 1000) + 60),
    },
    body: '{"message":"Forbidden"}',
    nowMs,
  });

  assert.equal(retryMs, 61_000);
});

test('uses Retry-After for GitHub 429 responses', () => {
  const retryMs = githubRateLimitRetryMs({
    status: 429,
    headers: { 'retry-after': '5' },
    body: '',
    nowMs: 1_700_000_000_000,
  });

  assert.equal(retryMs, 5_000);
});

test('does not retry ordinary GitHub authorization failures', () => {
  assert.equal(githubRateLimitRetryMs({
    status: 403,
    headers: { 'x-ratelimit-remaining': '42' },
    body: '{"message":"Resource not accessible by integration"}',
    nowMs: 1_700_000_000_000,
  }), null);

  assert.equal(githubRateLimitRetryMs({
    status: 401,
    headers: {},
    body: '{"message":"Bad credentials"}',
    nowMs: 1_700_000_000_000,
  }), null);
});

test('keeps rate-limit waits inside the existing eligibility deadline', () => {
  const nowMs = 1_700_000_000_000;
  assert.equal(boundedRateLimitWaitMs({
    retryMs: 5_000,
    deadlineMs: nowMs + 60_000,
    nowMs,
  }), 5_000);

  assert.equal(boundedRateLimitWaitMs({
    retryMs: 60_000,
    deadlineMs: nowMs + 60_000,
    nowMs,
  }), null);

  assert.equal(boundedRateLimitWaitMs({
    retryMs: 5_000,
    deadlineMs: nowMs,
    nowMs,
  }), null);
});
