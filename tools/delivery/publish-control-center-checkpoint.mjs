import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const DELIVERY_AGENT_ID = 'workslip-delivery';
const CI_PROVIDER = 'github-actions';
const CI_APPLICATION = 'workslip';
const CI_PROJECT = 'Workslip-v2.0';
const MAX_ATTEMPTS = 3;
const PIPELINES = {
  '.github/workflows/frontend-validation.yml': {
    key: 'ci',
    label: 'CI',
  },
  '.github/workflows/backend-production-deploy.yml': {
    key: 'backend-deploy',
    label: 'backend deployment',
  },
};

function requiredText(value, name) {
  const normalized = String(value ?? '').trim();
  if (!normalized) throw new Error(`${name} is required.`);
  return normalized;
}

function positiveInteger(value, fallback) {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function optionalSha(value) {
  const normalized = String(value ?? '').trim().toLowerCase();
  return /^[0-9a-f]{40}$/.test(normalized) ? normalized : undefined;
}

function optionalHttpsUrl(value) {
  const normalized = String(value ?? '').trim();
  if (!normalized) return undefined;

  try {
    const parsed = new URL(normalized);
    return parsed.protocol === 'https:' && !parsed.username && !parsed.password
      ? parsed.toString()
      : undefined;
  } catch {
    return undefined;
  }
}

function occurredAt(value, now) {
  const parsed = new Date(String(value ?? ''));
  return Number.isNaN(parsed.getTime()) ? now.toISOString() : parsed.toISOString();
}

function pullRequestNumber(workflowRun) {
  const candidate = workflowRun?.pull_requests?.[0]?.number;
  return Number.isSafeInteger(candidate) && candidate > 0 ? candidate : undefined;
}

function correlationScope(workflowRun, prNumber) {
  if (prNumber) return `pr-${prNumber}`;

  const branch = String(workflowRun.head_branch ?? '').trim();
  if (branch === 'main' || /^release(?:-|\/)/.test(branch)) return `branch-${branch}`;

  return `run-${requiredText(workflowRun.id, 'workflow run id')}`;
}

function pipelineFor(workflowRun) {
  const pipeline = PIPELINES[String(workflowRun.path ?? '').trim()];
  if (!pipeline) throw new Error('Workflow run is not an allowlisted delivery pipeline.');
  return pipeline;
}

function baseCheckpoint(workflowRun, repository, now) {
  const runId = requiredText(workflowRun.id, 'workflow run id');
  const attempt = positiveInteger(workflowRun.run_attempt, 1);
  const prNumber = pullRequestNumber(workflowRun);
  const pipeline = pipelineFor(workflowRun);

  return {
    id: `workslip-ci-${runId}-${attempt}`,
    occurredAt: occurredAt(workflowRun.updated_at, now),
    kind: 'Checkpoint',
    agentId: DELIVERY_AGENT_ID,
    provider: CI_PROVIDER,
    missionId: `github-${pipeline.key}-run-${runId}`,
    application: CI_APPLICATION,
    project: CI_PROJECT,
    environment: 'ci',
    issueReference: prNumber ? `PR-${prNumber}` : undefined,
    pullRequestReference: prNumber ? `#${prNumber}` : undefined,
    commitSha: optionalSha(workflowRun.head_sha),
    tool: CI_PROVIDER,
    evidenceReference: optionalHttpsUrl(workflowRun.html_url),
    correlationId: `github:${requiredText(repository, 'GitHub repository')}:${pipeline.key}:${correlationScope(workflowRun, prNumber)}`,
    pipeline,
  };
}

/**
 * Maps a GitHub workflow_run event to a sanitized Control Center checkpoint.
 * Cancellation and neutral outcomes intentionally do not resolve or create incidents.
 */
export function buildCheckpoint({ action, workflowRun, repository, now = new Date() }) {
  const run = workflowRun ?? {};
  const base = baseCheckpoint(run, repository, now);
  const attempt = positiveInteger(run.run_attempt, 1);
  const { pipeline, ...checkpoint } = base;

  if (action === 'in_progress') {
    return {
      ...checkpoint,
      id: `${checkpoint.id}-active`,
      state: 'Active',
      summary: attempt > 1
        ? `GitHub Actions ${pipeline.label} retry is running.`
        : `GitHub Actions ${pipeline.label} is running.`,
      reason: attempt > 1
        ? `The ${pipeline.label} workflow was restarted for another attempt.`
        : `The ${pipeline.label} workflow has started.`,
      nextAction: 'Wait for the delivery outcome; triage only an explicit failed checkpoint.',
    };
  }

  if (action !== 'completed') return null;

  const conclusion = String(run.conclusion ?? '').trim().toLowerCase();
  if (conclusion === 'success') {
    return {
      ...checkpoint,
      id: `${checkpoint.id}-completed`,
      state: 'Completed',
      summary: `GitHub Actions ${pipeline.label} completed successfully.`,
      reason: `The ${pipeline.label} workflow reported a successful conclusion.`,
      impact: `The linked GitHub Actions ${pipeline.label} run completed successfully.`,
      nextAction: 'No delivery recovery action is required.',
    };
  }

  if (['cancelled', 'skipped', 'neutral'].includes(conclusion)) return null;

  return {
    ...checkpoint,
    id: `${checkpoint.id}-failed`,
    state: 'Failed',
    summary: `GitHub Actions ${pipeline.label} failed.`,
    reason: `The ${pipeline.label} workflow concluded with ${conclusion || 'an unknown result'}.`,
    nextAction: 'Open the linked run and triage the failing job.',
  };
}

export function endpointFrom(value) {
  const endpoint = optionalHttpsUrl(value);
  if (!endpoint) throw new Error('MR_SAASY_ACTIVITY_URL must be an absolute HTTPS URL without embedded credentials.');
  return endpoint;
}

/**
 * Returns no headers when activation secrets are absent, which keeps the sender
 * disabled without manufacturing an unauthenticated request.
 */
export function authenticationHeaders({ activityToken, cloudflareClientId, cloudflareClientSecret }) {
  const token = String(activityToken ?? '').trim();
  const clientId = String(cloudflareClientId ?? '').trim();
  const clientSecret = String(cloudflareClientSecret ?? '').trim();

  if (Boolean(clientId) !== Boolean(clientSecret)) {
    if (!token) throw new Error('Cloudflare Access credentials must be configured as an ID/secret pair.');
    console.warn('[delivery] Incomplete Cloudflare Access credentials ignored because the activity token is configured.');
  }

  const headers = {
    Accept: 'application/json',
    'Content-Type': 'application/json',
    'User-Agent': 'workslip-delivery-checkpoint/1.0',
  };

  if (token) headers['X-MR-SAASY-ACTIVITY-TOKEN'] = token;
  if (clientId && clientSecret) {
    headers['CF-Access-Client-Id'] = clientId;
    headers['CF-Access-Client-Secret'] = clientSecret;
  }

  return token || (clientId && clientSecret) ? headers : null;
}

function retryableStatus(status) {
  return status === 408 || status === 429 || status >= 500;
}

function delay(milliseconds) {
  return new Promise(resolvePromise => setTimeout(resolvePromise, milliseconds));
}

/**
 * Delivers a sanitized checkpoint without ever reading or logging a response body.
 */
export async function publishCheckpoint({ endpoint, headers, checkpoint, fetchImpl = fetch, sleep = delay }) {
  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt += 1) {
    try {
      const response = await fetchImpl(endpoint, {
        method: 'POST',
        headers,
        body: JSON.stringify(checkpoint),
        signal: AbortSignal.timeout(10_000),
      });

      if (response.ok) return;
      if (!retryableStatus(response.status) || attempt === MAX_ATTEMPTS) {
        throw new Error(`Control Center checkpoint delivery returned HTTP ${response.status}.`);
      }
    } catch (error) {
      if (error instanceof Error && /^Control Center checkpoint delivery returned HTTP/.test(error.message)) throw error;
      if (attempt === MAX_ATTEMPTS) {
        throw new Error('Control Center checkpoint delivery failed after retries.');
      }
    }

    await sleep(attempt * 1_000);
  }
}

async function main() {
  const eventPath = requiredText(process.env.GITHUB_EVENT_PATH, 'GITHUB_EVENT_PATH');
  const event = JSON.parse(await readFile(eventPath, 'utf8'));
  const checkpoint = buildCheckpoint({
    action: event.action,
    workflowRun: event.workflow_run,
    repository: process.env.GITHUB_REPOSITORY,
  });

  if (!checkpoint) {
    console.log('[delivery] Delivery conclusion does not create or resolve a Bug Radar incident; no checkpoint published.');
    return;
  }

  const configuredUrl = String(process.env.MR_SAASY_ACTIVITY_URL ?? '').trim();
  if (!configuredUrl) {
    console.log('[delivery] Delivery checkpoint publishing is disabled: MR_SAASY_ACTIVITY_URL is not configured.');
    return;
  }

  const headers = authenticationHeaders({
    activityToken: process.env.MR_SAASY_ACTIVITY_TOKEN,
    cloudflareClientId: process.env.MR_SAASY_CF_ACCESS_CLIENT_ID,
    cloudflareClientSecret: process.env.MR_SAASY_CF_ACCESS_CLIENT_SECRET,
  });
  if (!headers) {
    console.log('[delivery] Delivery checkpoint publishing is disabled: no activity token or complete Cloudflare Access identity is configured.');
    return;
  }

  await publishCheckpoint({
    endpoint: endpointFrom(configuredUrl),
    headers,
    checkpoint,
  });
  console.log(`[delivery] Published ${checkpoint.state} delivery checkpoint ${checkpoint.id}.`);
}

const invokedPath = process.argv[1] ? resolve(process.argv[1]) : '';
const modulePath = resolve(fileURLToPath(import.meta.url));
if (invokedPath && invokedPath === modulePath) {
  main().catch((error) => {
    console.error(`[delivery] Delivery checkpoint publishing failed: ${error.message}`);
    process.exitCode = 1;
  });
}
