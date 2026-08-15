import assert from 'node:assert/strict';
import test from 'node:test';
import { chooseRun, validateEvidence } from './verify-production-eligibility.mjs';

const SHA = 'a'.repeat(40);
const OTHER_SHA = 'b'.repeat(40);

function greenRun(overrides = {}) {
  return {
    id: 101,
    name: 'CI',
    path: '.github/workflows/frontend-validation.yml',
    head_sha: SHA,
    head_branch: 'main',
    event: 'push',
    status: 'completed',
    conclusion: 'success',
    html_url: 'https://example.test/run/101',
    ...overrides,
  };
}

function greenGate(overrides = {}) {
  return {
    id: 202,
    name: 'CI Gate',
    status: 'completed',
    conclusion: 'success',
    html_url: 'https://example.test/job/202',
    ...overrides,
  };
}

test('accepts only an exact successful main CI Gate', () => {
  const evidence = validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [greenGate()] });
  assert.equal(evidence.sha, SHA);
  assert.equal(evidence.runId, 101);
});

test('rejects stale successful CI when main has advanced', () => {
  assert.throws(
    () => validateEvidence({ expectedSha: SHA, mainSha: OTHER_SHA, run: greenRun(), jobs: [greenGate()] }),
    /stale/,
  );
});

test('rejects every non-success workflow conclusion', () => {
  for (const conclusion of ['failure', 'cancelled', 'timed_out', 'action_required', 'neutral', 'skipped', null]) {
    assert.throws(
      () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun({ conclusion }), jobs: [greenGate()] }),
      /not green/,
    );
  }
});

test('rejects wrong branch and event', () => {
  assert.throws(
    () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun({ head_branch: 'feature' }), jobs: [greenGate()] }),
    /not main/,
  );
  assert.throws(
    () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun({ event: 'pull_request' }), jobs: [greenGate()] }),
    /not a main push/,
  );
});

test('rejects missing, duplicate, skipped or red CI Gate', () => {
  assert.throws(() => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [] }), /exactly one CI Gate/);
  assert.throws(
    () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [greenGate(), greenGate({ id: 203 })] }),
    /exactly one CI Gate/,
  );
  for (const conclusion of ['failure', 'cancelled', 'skipped', null]) {
    assert.throws(
      () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [greenGate({ conclusion })] }),
      /CI Gate is not green/,
    );
  }
});

test('run discovery prefers success, waits for active CI and fails closed otherwise', () => {
  assert.equal(chooseRun([greenRun()], SHA).state, 'success');
  assert.equal(chooseRun([greenRun({ status: 'in_progress', conclusion: null })], SHA).state, 'pending');
  assert.equal(chooseRun([greenRun({ conclusion: 'failure' })], SHA).state, 'failed');
  assert.equal(chooseRun([], SHA).state, 'missing');
});
