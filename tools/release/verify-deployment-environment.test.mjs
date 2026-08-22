import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { validateDeploymentEnvironment } from './verify-deployment-environment.mjs';

const SCRIPT = fileURLToPath(new URL('./verify-deployment-environment.mjs', import.meta.url));

function protectedEnvironment(overrides = {}) {
  return {
    name: 'live',
    can_admins_bypass: false,
    protection_rules: [
      { type: 'branch_policy' },
      {
        type: 'required_reviewers',
        reviewers: [{ type: 'User', reviewer: { id: 31623093 } }],
      },
    ],
    deployment_branch_policy: {
      protected_branches: false,
      custom_branch_policies: true,
    },
    ...overrides,
  };
}

function mainOnlyPolicy(overrides = {}) {
  return {
    total_count: 1,
    branch_policies: [
      { name: 'main', type: 'branch' },
    ],
    ...overrides,
  };
}

test('accepts a reviewer-protected main-only environment without admin bypass', () => {
  const evidence = validateDeploymentEnvironment({
    environment: protectedEnvironment(),
    branchPolicies: mainOnlyPolicy(),
    requiredBranch: 'main',
    requireReviewers: true,
    requireNoAdminBypass: true,
    requiredReviewerId: 31623093,
  });

  assert.deepEqual(evidence, {
    environment: 'live',
    branch: 'main',
    reviewerProtection: true,
    adminBypassDisabled: true,
  });
});

test('rejects a missing reviewer rule and administrator bypass', () => {
  assert.throws(
    () => validateDeploymentEnvironment({
      environment: protectedEnvironment({
        protection_rules: [{ type: 'branch_policy' }],
      }),
      branchPolicies: mainOnlyPolicy(),
      requiredBranch: 'main',
      requireReviewers: true,
    }),
    /must require an environment reviewer/,
  );

  assert.throws(
    () => validateDeploymentEnvironment({
      environment: protectedEnvironment({ can_admins_bypass: true }),
      branchPolicies: mainOnlyPolicy(),
      requiredBranch: 'main',
      requireNoAdminBypass: true,
    }),
    /disable administrator bypass/,
  );
});

test('rejects a reviewer rule that omits the repository owner', () => {
  assert.throws(
    () => validateDeploymentEnvironment({
      environment: protectedEnvironment({
        protection_rules: [
          { type: 'branch_policy' },
          {
            type: 'required_reviewers',
            reviewers: [{ type: 'User', reviewer: { id: 123 } }],
          },
        ],
      }),
      branchPolicies: mainOnlyPolicy(),
      requiredBranch: 'main',
      requiredReviewerId: 31623093,
    }),
    /must require repository-owner reviewer ID 31623093/,
  );
});

test('rejects missing, wildcard, additional, or wrong deployment branch policies', () => {
  for (const branchPolicies of [
    { total_count: 0, branch_policies: [] },
    { total_count: 1, branch_policies: [{ name: '*', type: 'branch' }] },
    { total_count: 1, branch_policies: [{ name: 'release/**', type: 'branch' }] },
    {
      total_count: 2,
      branch_policies: [
        { name: 'main', type: 'branch' },
        { name: 'release/**', type: 'branch' },
      ],
    },
    {
      total_count: 2,
      branch_policies: [{ name: 'main', type: 'branch' }],
    },
  ]) {
    assert.throws(
      () => validateDeploymentEnvironment({
        environment: protectedEnvironment(),
        branchPolicies,
        requiredBranch: 'main',
      }),
      /must allow exactly the main branch/,
    );
  }
});

test('rejects an environment without custom branch-policy protection', () => {
  assert.throws(
    () => validateDeploymentEnvironment({
      environment: protectedEnvironment({
        protection_rules: [{ type: 'required_reviewers' }],
      }),
      branchPolicies: mainOnlyPolicy(),
      requiredBranch: 'main',
    }),
    /no deployment branch policy protection/,
  );
  assert.throws(
    () => validateDeploymentEnvironment({
      environment: protectedEnvironment({
        deployment_branch_policy: {
          protected_branches: true,
          custom_branch_policies: false,
        },
      }),
      branchPolicies: mainOnlyPolicy(),
      requiredBranch: 'main',
    }),
    /must use a custom deployment branch policy/,
  );
});

test('CLI fails closed before network access when the environment is invalid', () => {
  const result = spawnSync(process.execPath, [
    SCRIPT,
    '--repository',
    'owner/repo',
    '--environment',
    '../live',
  ], {
    encoding: 'utf8',
    env: { ...process.env, GITHUB_TOKEN: '' },
  });

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /deployment environment blocked: Deployment environment name is missing or invalid/);
});
