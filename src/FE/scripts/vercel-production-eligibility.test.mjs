import assert from 'node:assert/strict';
import test from 'node:test';
import { chooseRun, parseGitHubRepository, validateGate } from './vercel-production-eligibility.mjs';

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
