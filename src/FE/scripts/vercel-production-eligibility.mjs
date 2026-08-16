import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const CI_WORKFLOW = 'frontend-validation.yml';
const REQUIRED_GATE = 'CI Gate';
const API_BASE = 'https://api.github.com';
const DEFAULT_RATE_LIMIT_RETRY_MS = 120_000;
const MIN_RATE_LIMIT_RETRY_MS = 1_000;

function requireSha(value) {
  const sha = String(value || '').trim().toLowerCase();
  if (!/^[0-9a-f]{40}$/.test(sha)) {
    throw new Error('Vercel production requires an exact 40-character Git commit SHA.');
  }
  return sha;
}

function git(...args) {
  try {
    return execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  } catch (error) {
    const message = error?.stderr?.toString?.().trim() || error?.message || 'unknown git error';
    throw new Error(`Unable to read deployment Git metadata: ${message}`);
  }
}

export function parseGitHubRepository(remoteUrl) {
  const value = String(remoteUrl || '').trim();
  const match = value.match(/github\.com[/:]([^/\s]+)\/([^/\s]+?)(?:\.git)?$/i);
  if (!match) throw new Error('Deployment Git remote is not a GitHub repository.');
  return `${match[1]}/${match[2]}`;
}

function resolveRepository() {
  const owner = process.env.VERCEL_GIT_REPO_OWNER;
  const repo = process.env.VERCEL_GIT_REPO_SLUG;
  if (owner && repo && !/[\s/]/.test(owner) && !/[\s/]/.test(repo)) {
    return `${owner}/${repo}`;
  }
  return parseGitHubRepository(git('config', '--get', 'remote.origin.url'));
}

function resolveSha() {
  return requireSha(process.env.VERCEL_GIT_COMMIT_SHA || git('rev-parse', 'HEAD'));
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

function getHeader(headers, name) {
  if (!headers) return '';
  if (typeof headers.get === 'function') return headers.get(name) || '';

  const wanted = name.toLowerCase();
  const entry = Object.entries(headers).find(([key]) => key.toLowerCase() === wanted);
  return entry ? String(entry[1] ?? '') : '';
}

function retryAfterMs(value, nowMs) {
  const text = String(value || '').trim();
  if (!text) return null;

  const seconds = Number(text);
  if (Number.isFinite(seconds) && seconds >= 0) {
    return Math.max(MIN_RATE_LIMIT_RETRY_MS, Math.ceil(seconds * 1000));
  }

  const retryAt = Date.parse(text);
  if (Number.isFinite(retryAt)) {
    return Math.max(MIN_RATE_LIMIT_RETRY_MS, retryAt - nowMs);
  }

  return null;
}

export function githubRateLimitRetryMs({
  status,
  headers,
  body,
  nowMs = Date.now(),
  fallbackMs = DEFAULT_RATE_LIMIT_RETRY_MS,
}) {
  const statusCode = Number(status);
  const remaining = String(getHeader(headers, 'x-ratelimit-remaining')).trim();
  const responseBody = String(body || '').toLowerCase();
  const isRateLimited = statusCode === 429
    || (statusCode === 403 && (remaining === '0' || responseBody.includes('rate limit exceeded')));

  if (!isRateLimited) return null;

  const retryAfter = retryAfterMs(getHeader(headers, 'retry-after'), nowMs);
  if (retryAfter !== null) return retryAfter;

  const resetSeconds = Number(String(getHeader(headers, 'x-ratelimit-reset')).trim());
  if (Number.isFinite(resetSeconds) && resetSeconds > 0) {
    const resetMs = (resetSeconds * 1000) - nowMs + 1000;
    return Math.max(MIN_RATE_LIMIT_RETRY_MS, Math.ceil(resetMs));
  }

  const safeFallback = Number(fallbackMs);
  return Math.max(
    MIN_RATE_LIMIT_RETRY_MS,
    Number.isFinite(safeFallback) && safeFallback > 0 ? Math.ceil(safeFallback) : DEFAULT_RATE_LIMIT_RETRY_MS,
  );
}

export function boundedRateLimitWaitMs({ retryMs, deadlineMs, nowMs = Date.now() }) {
  const requestedMs = Number(retryMs);
  const remainingMs = Number(deadlineMs) - Number(nowMs);
  if (!Number.isFinite(requestedMs) || requestedMs <= 0 || !Number.isFinite(remainingMs) || remainingMs <= 0) {
    return null;
  }

  const waitMs = Math.max(MIN_RATE_LIMIT_RETRY_MS, Math.ceil(requestedMs));
  return waitMs < remainingMs ? waitMs : null;
}

class GitHubRateLimitError extends Error {
  constructor(message, retryMs) {
    super(message);
    this.name = 'GitHubRateLimitError';
    this.retryMs = retryMs;
  }
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
    const rateLimitRetryMs = githubRateLimitRetryMs({
      status: response.status,
      headers: response.headers,
      body,
    });
    if (rateLimitRetryMs !== null) {
      throw new GitHubRateLimitError(`GitHub API ${response.status} rate limited the production gate.`, rateLimitRetryMs);
    }
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

export function productionGateMode({ vercelEnv, commitRef } = {}) {
  const environment = String(vercelEnv || '').trim();
  const ref = String(commitRef || '').trim();

  if (environment === 'preview' || environment === 'development') {
    return { enforce: false, environment };
  }

  if (environment && environment !== 'production') {
    throw new Error(`Unsupported VERCEL_ENV=${environment}; refusing to guess deployment intent.`);
  }

  if (ref && ref !== 'main') {
    throw new Error(`Vercel production may only release main, got ${ref}.`);
  }

  return { enforce: true, environment: environment || 'manual' };
}

async function main() {
  const mode = productionGateMode({
    vercelEnv: process.env.VERCEL_ENV,
    commitRef: process.env.VERCEL_GIT_COMMIT_REF,
  });

  if (!mode.enforce) {
    console.log(`[release] Vercel ${mode.environment} build: production eligibility gate skipped; continuing normal build.`);
    return;
  }

  const sha = resolveSha();
  const repository = resolveRepository();
  const waitSeconds = 1800;
  const pollSeconds = 120;
  const deadline = Date.now() + waitSeconds * 1000;

  while (true) {
    try {
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
    } catch (error) {
      if (!(error instanceof GitHubRateLimitError)) throw error;

      const waitMs = boundedRateLimitWaitMs({ retryMs: error.retryMs, deadlineMs: deadline });
      if (waitMs === null) {
        throw new Error(`Timed out waiting for GitHub API rate-limit recovery while validating main @ ${sha}.`);
      }

      console.log(`[release] GitHub API rate limited; waiting ${Math.ceil(waitMs / 1000)}s before re-checking exact main/CI evidence.`);
      await sleep(waitMs);
    }
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
