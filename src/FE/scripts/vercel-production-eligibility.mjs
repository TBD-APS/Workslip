import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const CI_WORKFLOW = 'frontend-validation.yml';
const REQUIRED_GATE = 'CI Gate';
const API_BASE = 'https://api.github.com';

function requireSha(value) {
  const sha = String(value || '').trim().toLowerCase();
  if (!/^[0-9a-f]{40}$/.test(sha)) {
    throw new Error('Vercel production requires an exact 40-character Git commit SHA.');
  }
  return sha;
}

function requireRepository(owner, repo) {
  if (!owner || !repo || /[\s/]/.test(owner) || /[\s/]/.test(repo)) {
    throw new Error('Vercel Git repository metadata is missing or invalid.');
  }
  return `${owner}/${repo}`;
}

export function chooseRun(runs, expectedSha) {
  const sha = requireSha(expectedSha);
  const matching = (runs || []).filter((run) =>
    String(run.head_sha || '').toLowerCase() === sha
    && run.head_branch === 'main'
    && run.event === 'push');

  const successful = matching.find((run) => run.status === 'completed' && run.conclusion === 'success');
  if (successful) return { state: 'success', run: successful };
  if (matching.some((run) => ['queued', 'in_progress', 'waiting', 'requested', 'pending'].includes(run.status))) {
    return { state: 'pending', run: null };
  }
  if (matching.length > 0) return { state: 'failed', run: matching[0] };
  return { state: 'missing', run: null };
}

export function validateGate({ expectedSha, mainSha, run, jobs }) {
  const sha = requireSha(expectedSha);
  if (requireSha(mainSha) !== sha) {
    throw new Error(`Production candidate ${sha} is stale; current main is ${mainSha}.`);
  }
  if (!run || String(run.head_sha || '').toLowerCase() !== sha) {
    throw new Error(`No successful exact-SHA CI run exists for ${sha}.`);
  }
  if (run.head_branch !== 'main' || run.event !== 'push') {
    throw new Error('Production CI evidence must be a push run on main.');
  }
  if (run.status !== 'completed' || run.conclusion !== 'success') {
    throw new Error(`CI is not green: status=${run.status || 'unknown'}, conclusion=${run.conclusion || 'unknown'}.`);
  }

  const gates = (jobs || []).filter((job) => job.name === REQUIRED_GATE);
  if (gates.length !== 1) {
    throw new Error(`Expected exactly one ${REQUIRED_GATE} job, found ${gates.length}.`);
  }
  const gate = gates[0];
  if (gate.status !== 'completed' || gate.conclusion !== 'success') {
    throw new Error(`${REQUIRED_GATE} is not green: status=${gate.status || 'unknown'}, conclusion=${gate.conclusion || 'unknown'}.`);
  }

  return { sha, runId: run.id, gateId: gate.id };
}

async function api(repository, path) {
  const response = await fetch(`${API_BASE}/repos/${repository}${path}`, {
    headers: {
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'workslip-vercel-production-gate',
    },
  });
  if (!response.ok) {
    const body = await response.text();
    throw new Error(`GitHub API ${response.status}: ${body.slice(0, 300)}`);
  }
  return response.json();
}

async function currentMainSha(repository) {
  const ref = await api(repository, '/git/ref/heads/main');
  return requireSha(ref?.object?.sha);
}

async function workflowRuns(repository, sha) {
  const query = new URLSearchParams({ branch: 'main', event: 'push', head_sha: sha, per_page: '20' });
  const response = await api(repository, `/actions/workflows/${CI_WORKFLOW}/runs?${query}`);
  return response.workflow_runs || [];
}

async function jobs(repository, runId) {
  const response = await api(repository, `/actions/runs/${runId}/jobs?filter=latest&per_page=100`);
  return response.jobs || [];
}

function sleep(ms) {
  return new Promise((resolvePromise) => setTimeout(resolvePromise, ms));
}

async function main() {
  if (process.env.VERCEL_ENV !== 'production') {
    throw new Error(`Production gate invoked for VERCEL_ENV=${process.env.VERCEL_ENV || 'unset'}.`);
  }
  if (process.env.VERCEL_GIT_COMMIT_REF !== 'main') {
    throw new Error(`Vercel production may only release main, got ${process.env.VERCEL_GIT_COMMIT_REF || 'unset'}.`);
  }

  const sha = requireSha(process.env.VERCEL_GIT_COMMIT_SHA);
  const repository = requireRepository(process.env.VERCEL_GIT_REPO_OWNER, process.env.VERCEL_GIT_REPO_SLUG);
  const waitSeconds = 1800;
  const pollSeconds = 120;
  const deadline = Date.now() + waitSeconds * 1000;

  while (true) {
    const mainBefore = await currentMainSha(repository);
    if (mainBefore !== sha) {
      throw new Error(`Production candidate ${sha} is stale; current main is ${mainBefore}.`);
    }

    const selected = chooseRun(await workflowRuns(repository, sha), sha);
    if (selected.state === 'failed') {
      throw new Error(`CI for main @ ${sha} completed without success (conclusion=${selected.run?.conclusion || 'unknown'}).`);
    }

    if (selected.state === 'success') {
      const evidence = validateGate({
        expectedSha: sha,
        mainSha: mainBefore,
        run: selected.run,
        jobs: await jobs(repository, selected.run.id),
      });
      const mainAfter = await currentMainSha(repository);
      if (mainAfter !== sha) {
        throw new Error(`Production candidate ${sha} became stale while verifying CI; current main is ${mainAfter}.`);
      }
      console.log(`[release] Vercel production eligible: main @ ${sha}; CI run ${evidence.runId}; ${REQUIRED_GATE} ${evidence.gateId}.`);
      return;
    }

    if (Date.now() >= deadline) {
      throw new Error(`Timed out waiting for a successful ${REQUIRED_GATE} for main @ ${sha}.`);
    }

    console.log(`[release] CI for ${sha.slice(0, 12)} is ${selected.state}; waiting ${pollSeconds}s.`);
    await sleep(pollSeconds * 1000);
  }
}

const invokedPath = process.argv[1] ? resolve(process.argv[1]) : '';
const modulePath = resolve(fileURLToPath(import.meta.url));
if (invokedPath && invokedPath === modulePath) {
  main().catch((error) => {
    console.error(`[release] Vercel production blocked: ${error.message}`);
    process.exitCode = 1;
  });
}
