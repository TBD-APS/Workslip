import fs from 'node:fs';
import process from 'node:process';

const DEFAULT_API_BASE = 'https://api.github.com';
const CI_WORKFLOW = 'frontend-validation.yml';
const REQUIRED_GATE = 'CI Gate';

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

function asNonNegativeInteger(value, fallback, name) {
  if (value == null || value === '') return fallback;
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < 0) {
    throw new Error(`${name} must be a non-negative integer.`);
  }
  return parsed;
}

function requireSha(value) {
  const sha = String(value || '').trim().toLowerCase();
  if (!/^[0-9a-f]{40}$/.test(sha)) {
    throw new Error('Production eligibility requires an exact 40-character commit SHA.');
  }
  return sha;
}

function resolveRepository(explicit) {
  const repository = explicit
    || process.env.GITHUB_REPOSITORY
    || (process.env.VERCEL_GIT_REPO_OWNER && process.env.VERCEL_GIT_REPO_SLUG
      ? `${process.env.VERCEL_GIT_REPO_OWNER}/${process.env.VERCEL_GIT_REPO_SLUG}`
      : '');
  if (!/^[^/\s]+\/[^/\s]+$/.test(repository)) {
    throw new Error('Unable to resolve GitHub repository in owner/name form.');
  }
  return repository;
}

function resolveSha(explicit, source) {
  const candidate = explicit
    || (source === 'vercel' ? process.env.VERCEL_GIT_COMMIT_SHA : process.env.GITHUB_SHA);
  return requireSha(candidate);
}

function assertSourceBoundary(source) {
  if (source === 'vercel') {
    if (process.env.VERCEL_ENV && process.env.VERCEL_ENV !== 'production') {
      throw new Error(`Vercel production eligibility was invoked for VERCEL_ENV=${process.env.VERCEL_ENV}.`);
    }
    if (process.env.VERCEL_GIT_COMMIT_REF && process.env.VERCEL_GIT_COMMIT_REF !== 'main') {
      throw new Error(`Vercel production may only release main, got ${process.env.VERCEL_GIT_COMMIT_REF}.`);
    }
  }
}

export function validateEvidence({ expectedSha, mainSha, run, jobs }) {
  const sha = requireSha(expectedSha);
  if (requireSha(mainSha) !== sha) {
    throw new Error(`Validated SHA ${sha} is stale; current main is ${mainSha}.`);
  }
  if (!run) throw new Error(`No CI run exists for main @ ${sha}.`);
  if (String(run.head_sha || '').toLowerCase() !== sha) {
    throw new Error(`CI run ${run.id ?? 'unknown'} reviewed ${run.head_sha || 'no SHA'}, expected ${sha}.`);
  }
  if (run.head_branch !== 'main') {
    throw new Error(`CI run ${run.id ?? 'unknown'} is for branch ${run.head_branch || 'unknown'}, not main.`);
  }
  if (run.event !== 'push') {
    throw new Error(`CI run ${run.id ?? 'unknown'} was triggered by ${run.event || 'unknown'}, not a main push.`);
  }
  if (run.status !== 'completed' || run.conclusion !== 'success') {
    throw new Error(`CI run ${run.id ?? 'unknown'} is not green: status=${run.status || 'unknown'}, conclusion=${run.conclusion || 'unknown'}.`);
  }

  const gates = (jobs || []).filter((job) => job.name === REQUIRED_GATE);
  if (gates.length !== 1) {
    throw new Error(`Expected exactly one ${REQUIRED_GATE} job, found ${gates.length}.`);
  }
  const gate = gates[0];
  if (gate.status !== 'completed' || gate.conclusion !== 'success') {
    throw new Error(`${REQUIRED_GATE} is not green: status=${gate.status || 'unknown'}, conclusion=${gate.conclusion || 'unknown'}.`);
  }

  return {
    sha,
    runId: run.id,
    runUrl: run.html_url || '',
    gateId: gate.id,
    gateUrl: gate.html_url || '',
  };
}

export function chooseRun(runs, expectedSha) {
  const sha = requireSha(expectedSha);
  const matching = (runs || []).filter((run) =>
    String(run.head_sha || '').toLowerCase() === sha
    && run.head_branch === 'main'
    && run.event === 'push');
  const successful = matching.find((run) => run.status === 'completed' && run.conclusion === 'success');
  if (successful) return { state: 'success', run: successful };
  if (matching.some((run) => run.status === 'queued' || run.status === 'in_progress' || run.status === 'waiting' || run.status === 'requested' || run.status === 'pending')) {
    return { state: 'pending', run: null };
  }
  if (matching.length > 0) {
    const latest = matching[0];
    return { state: 'failed', run: latest };
  }
  return { state: 'missing', run: null };
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function createClient(apiBase, token) {
  const base = apiBase.replace(/\/$/, '');
  return async (path) => {
    const response = await fetch(`${base}${path}`, {
      headers: {
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'workslip-production-eligibility',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    });
    if (!response.ok) {
      const body = await response.text();
      throw new Error(`GitHub API ${response.status} for ${path}: ${body.slice(0, 300)}`);
    }
    return response.json();
  };
}

async function loadMainSha(client, repository) {
  const ref = await client(`/repos/${repository}/git/ref/heads/main`);
  return requireSha(ref?.object?.sha);
}

async function loadJobs(client, repository, runId) {
  const response = await client(`/repos/${repository}/actions/runs/${runId}/jobs?filter=latest&per_page=100`);
  return response.jobs || [];
}

async function loadRunById(client, repository, runId) {
  const run = await client(`/repos/${repository}/actions/runs/${runId}`);
  if (run.name !== 'CI' || !String(run.path || '').endsWith(`/${CI_WORKFLOW}`)) {
    throw new Error(`Workflow run ${runId} is ${run.name || 'unknown'} (${run.path || 'unknown'}), not the canonical CI workflow.`);
  }
  return run;
}

async function discoverRun(client, repository, sha) {
  const response = await client(`/repos/${repository}/actions/workflows/${CI_WORKFLOW}/runs?branch=main&event=push&head_sha=${sha}&per_page=20`);
  return chooseRun(response.workflow_runs || [], sha);
}

async function resolveEvidence({ client, repository, sha, ciRunId, waitSeconds, pollSeconds }) {
  const deadline = Date.now() + waitSeconds * 1000;
  while (true) {
    const mainSha = await loadMainSha(client, repository);
    if (mainSha !== sha) {
      throw new Error(`Production candidate ${sha} is stale; current main is ${mainSha}.`);
    }

    if (ciRunId) {
      const run = await loadRunById(client, repository, ciRunId);
      const jobs = await loadJobs(client, repository, ciRunId);
      return validateEvidence({ expectedSha: sha, mainSha, run, jobs });
    }

    const discovered = await discoverRun(client, repository, sha);
    if (discovered.state === 'success') {
      const jobs = await loadJobs(client, repository, discovered.run.id);
      return validateEvidence({ expectedSha: sha, mainSha, run: discovered.run, jobs });
    }
    if (discovered.state === 'failed') {
      throw new Error(`CI for main @ ${sha} completed without success (conclusion=${discovered.run?.conclusion || 'unknown'}).`);
    }
    if (Date.now() >= deadline) {
      throw new Error(`Timed out waiting for a successful ${REQUIRED_GATE} for main @ ${sha}.`);
    }

    console.log(`[release] CI for ${sha.slice(0, 12)} is ${discovered.state}; waiting ${pollSeconds}s.`);
    await sleep(pollSeconds * 1000);
  }
}

function publishEvidence(evidence) {
  console.log(`[release] production eligible: main @ ${evidence.sha}; CI run ${evidence.runId}; ${REQUIRED_GATE} ${evidence.gateId}.`);

  if (process.env.GITHUB_OUTPUT) {
    fs.appendFileSync(process.env.GITHUB_OUTPUT, `validated_sha=${evidence.sha}\nci_run_id=${evidence.runId}\n`);
  }
  if (process.env.GITHUB_STEP_SUMMARY) {
    const runLink = evidence.runUrl ? `[${evidence.runId}](${evidence.runUrl})` : String(evidence.runId);
    const gateLink = evidence.gateUrl ? `[${REQUIRED_GATE}](${evidence.gateUrl})` : REQUIRED_GATE;
    fs.appendFileSync(
      process.env.GITHUB_STEP_SUMMARY,
      `\n### Production eligibility\n\n- SHA: \`${evidence.sha}\`\n- CI run: ${runLink}\n- Gate: ${gateLink} — success\n`,
    );
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const source = args.get('source') || (process.env.VERCEL ? 'vercel' : 'actions');
  if (!['actions', 'vercel'].includes(source)) {
    throw new Error(`Unsupported source: ${source}.`);
  }
  assertSourceBoundary(source);

  const repository = resolveRepository(args.get('repository'));
  const sha = resolveSha(args.get('sha'), source);
  const ciRunId = args.get('ci-run-id') ? asNonNegativeInteger(args.get('ci-run-id'), 0, 'ci-run-id') : 0;
  const waitSeconds = asNonNegativeInteger(args.get('wait-seconds'), 0, 'wait-seconds');
  const pollSeconds = Math.max(5, asNonNegativeInteger(args.get('poll-seconds'), 45, 'poll-seconds'));
  const apiBase = args.get('api-base') || process.env.GITHUB_API_URL || DEFAULT_API_BASE;
  const token = process.env.GITHUB_TOKEN || '';

  const evidence = await resolveEvidence({
    client: createClient(apiBase, token),
    repository,
    sha,
    ciRunId,
    waitSeconds,
    pollSeconds,
  });
  publishEvidence(evidence);
}

if (process.argv[1] && new URL(import.meta.url).pathname === process.argv[1]) {
  main().catch((error) => {
    console.error(`[release] production blocked: ${error.message}`);
    process.exitCode = 1;
  });
}
