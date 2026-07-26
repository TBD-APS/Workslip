import assert from 'node:assert/strict';
import test from 'node:test';

import {
  BUILD,
  SKIP,
  decideVercelBuild,
} from './vercel-build-policy.mjs';

const previousSha = '1'.repeat(40);
const currentSha = '2'.repeat(40);

test('skips automatic preview builds', () => {
  const decision = decideVercelBuild({
    environment: 'preview',
    branch: 'rbj--wor-177-vercel-build-policy',
  });

  assert.equal(decision.exitCode, SKIP);
});

test('skips a production deployment from a branch other than main', () => {
  const decision = decideVercelBuild({
    environment: 'production',
    branch: 'release-candidate',
  });

  assert.equal(decision.exitCode, SKIP);
});

test('builds main when frontend files changed', () => {
  const decision = decideVercelBuild({
    environment: 'production',
    branch: 'main',
    previousSha,
    currentSha,
    diffStatus: 1,
  });

  assert.equal(decision.exitCode, BUILD);
});

test('skips main when frontend files are unchanged', () => {
  const decision = decideVercelBuild({
    environment: 'production',
    branch: 'main',
    previousSha,
    currentSha,
    diffStatus: 0,
  });

  assert.equal(decision.exitCode, SKIP);
});

test('builds production when comparison SHAs are unavailable', () => {
  const decision = decideVercelBuild({
    environment: 'production',
    branch: 'main',
    previousSha: '',
    currentSha,
    diffStatus: null,
  });

  assert.equal(decision.exitCode, BUILD);
});

test('builds production when the Git comparison fails', () => {
  const decision = decideVercelBuild({
    environment: 'production',
    branch: 'main',
    previousSha,
    currentSha,
    diffStatus: 128,
  });

  assert.equal(decision.exitCode, BUILD);
});

test('skips an already deployed production commit', () => {
  const decision = decideVercelBuild({
    environment: 'production',
    branch: 'main',
    previousSha,
    currentSha: previousSha,
    diffStatus: null,
  });

  assert.equal(decision.exitCode, SKIP);
});
