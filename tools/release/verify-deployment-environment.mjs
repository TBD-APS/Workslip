import process from 'node:process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const DEFAULT_API_BASE = 'https://api.github.com';

function parseArgs(argv) {
  const values = new Map();
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (!arg.startsWith('--')) continue;
    const key = arg.slice(2);
    const next = argv[index + 1];
    if (next && !next.startsWith('--')) {
      values.set(key, next);
      index += 1;
    } else {
      values.set(key, 'true');
    }
  }
  return values;
}

function requireRepository(value) {
  const repository = String(value || '').trim();
  if (!/^[^/\s]+\/[^/\s]+$/.test(repository)) {
    throw new Error('Deployment-environment verification requires owner/name repository syntax.');
  }
  return repository;
}

function requireEnvironmentName(value) {
  const environment = String(value || '').trim();
  if (!/^[A-Za-z0-9_.-]+$/.test(environment)) {
    throw new Error('Deployment environment name is missing or invalid.');
  }
  return environment;
}

function requireBranchName(value) {
  const branch = String(value || '').trim();
  if (!/^[A-Za-z0-9._/-]+$/.test(branch)) {
    throw new Error('Deployment branch name is missing or invalid.');
  }
  return branch;
}

function optionalPositiveInteger(value, name) {
  if (value == null || value === '') return null;
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed <= 0) {
    throw new Error(`${name} must be a positive integer.`);
  }
  return parsed;
}

export function validateDeploymentEnvironment({
  environment,
  branchPolicies,
  requiredBranch,
  requireReviewers = false,
  requireNoAdminBypass = false,
  requiredReviewerId = null,
}) {
  const name = requireEnvironmentName(environment?.name);
  const branch = requireBranchName(requiredBranch);
  const protectionRules = Array.isArray(environment?.protection_rules)
    ? environment.protection_rules
    : [];

  if (!protectionRules.some((rule) => rule?.type === 'branch_policy')) {
    throw new Error(`GitHub environment ${name} has no deployment branch policy protection.`);
  }
  if (environment?.deployment_branch_policy?.custom_branch_policies !== true) {
    throw new Error(`GitHub environment ${name} must use a custom deployment branch policy.`);
  }

  const policies = Array.isArray(branchPolicies?.branch_policies)
    ? branchPolicies.branch_policies
    : [];
  const totalPolicyCount = Number(branchPolicies?.total_count);
  const exactBranches = policies.filter((policy) =>
    policy?.type === 'branch' && policy?.name === branch);
  if (totalPolicyCount !== 1 || policies.length !== 1 || exactBranches.length !== 1) {
    throw new Error(
      `GitHub environment ${name} must allow exactly the ${branch} branch; GitHub reports ${Number.isFinite(totalPolicyCount) ? totalPolicyCount : 'an invalid number of'} deployment branch policies.`,
    );
  }

  const reviewerRule = protectionRules.find((rule) => rule?.type === 'required_reviewers');
  if (requireReviewers && !reviewerRule) {
    throw new Error(`GitHub environment ${name} must require an environment reviewer.`);
  }
  if (requiredReviewerId != null) {
    const reviewerIds = (Array.isArray(reviewerRule?.reviewers) ? reviewerRule.reviewers : [])
      .map((entry) => Number(entry?.reviewer?.id))
      .filter(Number.isSafeInteger);
    if (!reviewerIds.includes(requiredReviewerId)) {
      throw new Error(
        `GitHub environment ${name} must require repository-owner reviewer ID ${requiredReviewerId}.`,
      );
    }
  }
  if (requireNoAdminBypass && environment?.can_admins_bypass !== false) {
    throw new Error(`GitHub environment ${name} must disable administrator bypass.`);
  }

  return {
    environment: name,
    branch,
    reviewerProtection: protectionRules.some((rule) => rule?.type === 'required_reviewers'),
    adminBypassDisabled: environment?.can_admins_bypass === false,
  };
}

function createClient(apiBase, token) {
  const base = apiBase.replace(/\/$/, '');
  return async (path) => {
    const response = await fetch(`${base}${path}`, {
      headers: {
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'workslip-deployment-environment-verifier',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    });
    if (!response.ok) {
      const body = await response.text();
      if (response.status === 404) {
        throw new Error(
          `GitHub deployment environment is missing or unreadable at ${path}. Configure it before dispatch; workflows must not auto-create an unprotected environment.`,
        );
      }
      throw new Error(`GitHub API ${response.status} for ${path}: ${body.slice(0, 300)}`);
    }
    return response.json();
  };
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const repository = requireRepository(args.get('repository') || process.env.GITHUB_REPOSITORY);
  const environmentName = requireEnvironmentName(args.get('environment'));
  const requiredBranch = requireBranchName(args.get('branch') || 'main');
  const apiBase = args.get('api-base') || process.env.GITHUB_API_URL || DEFAULT_API_BASE;
  const requiredReviewerId = optionalPositiveInteger(
    args.get('required-reviewer-id'),
    'required-reviewer-id',
  );
  const client = createClient(apiBase, process.env.GITHUB_TOKEN || '');
  const encodedEnvironment = encodeURIComponent(environmentName);

  const environment = await client(
    `/repos/${repository}/environments/${encodedEnvironment}`,
  );
  const branchPolicies = await client(
    `/repos/${repository}/environments/${encodedEnvironment}/deployment-branch-policies`,
  );
  const evidence = validateDeploymentEnvironment({
    environment,
    branchPolicies,
    requiredBranch,
    requireReviewers: args.has('require-reviewers') || requiredReviewerId != null,
    requireNoAdminBypass: args.has('require-no-admin-bypass'),
    requiredReviewerId,
  });

  console.log(
    `[release] deployment environment verified: ${evidence.environment}; branch=${evidence.branch}; reviewers=${evidence.reviewerProtection}; admin-bypass-disabled=${evidence.adminBypassDisabled}.`,
  );
}

const invokedPath = process.argv[1] ? resolve(process.argv[1]) : '';
const modulePath = resolve(fileURLToPath(import.meta.url));
if (invokedPath && invokedPath === modulePath) {
  main().catch((error) => {
    console.error(`[release] deployment environment blocked: ${error.message}`);
    process.exitCode = 1;
  });
}
