import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export const BUILD = 1;
export const SKIP = 0;

const fullGitShaPattern = /^[0-9a-f]{40}$/i;

export function decideVercelBuild({
  environment,
  branch,
  previousSha,
  currentSha,
  diffStatus,
}) {
  if (environment !== 'production' || branch !== 'main') {
    return {
      exitCode: SKIP,
      reason: 'automatic preview builds are disabled; production deploys from main',
    };
  }

  if (
    !fullGitShaPattern.test(previousSha ?? '') ||
    !fullGitShaPattern.test(currentSha ?? '')
  ) {
    return {
      exitCode: BUILD,
      reason: 'Git comparison data is unavailable; building production fail-open',
    };
  }

  if (previousSha === currentSha || diffStatus === 0) {
    return {
      exitCode: SKIP,
      reason: 'src/FE is unchanged since the last successful production deployment',
    };
  }

  if (diffStatus === 1) {
    return {
      exitCode: BUILD,
      reason: 'src/FE changed; building production',
    };
  }

  return {
    exitCode: BUILD,
    reason: 'Git comparison failed; building production fail-open',
  };
}

function getFrontendDiffStatus(previousSha, currentSha) {
  if (
    !fullGitShaPattern.test(previousSha ?? '') ||
    !fullGitShaPattern.test(currentSha ?? '') ||
    previousSha === currentSha
  ) {
    return null;
  }

  const result = spawnSync(
    'git',
    ['diff', '--quiet', previousSha, currentSha, '--', ':(top)src/FE'],
    {
      cwd: process.cwd(),
      stdio: 'ignore',
    },
  );

  return result.error ? null : result.status;
}

export function runVercelBuildPolicy(environment = process.env) {
  const previousSha = environment.VERCEL_GIT_PREVIOUS_SHA;
  const currentSha = environment.VERCEL_GIT_COMMIT_SHA;
  const diffStatus = getFrontendDiffStatus(previousSha, currentSha);
  const decision = decideVercelBuild({
    environment: environment.VERCEL_ENV,
    branch: environment.VERCEL_GIT_COMMIT_REF,
    previousSha,
    currentSha,
    diffStatus,
  });

  console.log(`[vercel-build-policy] ${decision.reason}`);
  return decision.exitCode;
}

const isDirectExecution =
  process.argv[1] &&
  import.meta.url === pathToFileURL(process.argv[1]).href;

if (isDirectExecution) {
  process.exitCode = runVercelBuildPolicy();
}
