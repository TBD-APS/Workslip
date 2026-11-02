// Exact-source release admission. Products supply all repository and CI policy.
const PAGE_SIZE = 100;
const MAX_EVIDENCE_ITEMS = 1000;
const ACTIVE_RUN_STATES = new Set(['queued', 'in_progress', 'waiting', 'requested', 'pending']);
const RELEASE_EVENTS = new Set(['push', 'workflow_dispatch']);

function positiveInteger(value) {
  return Number.isSafeInteger(value) && value > 0;
}

function requireSha(value) {
  if (typeof value !== 'string' || !/^[a-f0-9]{40}$/.test(value)) {
    throw Error('Production eligibility requires an exact 40-character commit SHA.');
  }
  return value;
}

function nonemptyStrings(values) {
  return Array.isArray(values) && values.length > 0
    && values.every(value => typeof value === 'string' && value.trim() === value && value.length > 0)
    && new Set(values).size === values.length;
}

function mergePolicy(policy) {
  if (!policy || typeof policy !== 'object'
      || typeof policy.repository !== 'string' || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(policy.repository)
      || policy.repository.split('/').some(part => part === '.' || part === '..')
      || typeof policy.branch !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9._/-]*$/.test(policy.branch)
      || policy.branch.includes('..') || policy.branch.split('/').some(part => !part || part.endsWith('.') || part.endsWith('.lock'))
      || !Array.isArray(policy.mergerIds) || !policy.mergerIds.length || !policy.mergerIds.every(positiveInteger)
      || new Set(policy.mergerIds).size !== policy.mergerIds.length
      || !nonemptyStrings(policy.events) || !policy.events.every(event => RELEASE_EVENTS.has(event))) {
    throw Error('An explicit repository, branch, numeric merger IDs and events policy is required.');
  }
  return { repository: policy.repository, branch: policy.branch, mergerIds: [...policy.mergerIds], events: [...policy.events] };
}

function requireRepository(repository, policy) {
  if (repository !== policy.repository) throw Error(`Production ownership requires ${policy.repository}.`);
}

async function requireCurrentBranch(client, policy, sha, duringVerification = false) {
  const ref = await client(`/repos/${policy.repository}/git/ref/heads/${policy.branch}`);
  if (ref?.object?.sha !== sha) {
    throw Error(`Production candidate ${sha} is stale${duringVerification ? ' while verifying evidence' : ''}; current ${policy.branch} is ${ref?.object?.sha || 'unknown'}.`);
  }
}

async function ownerMerge(client, policy, sha) {
  const base = `/repos/${policy.repository}`;
  const associated = await client(`${base}/commits/${sha}/pulls?per_page=${PAGE_SIZE}`);
  // The array-only endpoint supplies no total. A full page cannot prove completeness.
  if (!Array.isArray(associated) || !associated.length || associated.length >= PAGE_SIZE) {
    throw Error('Complete merged PR evidence is required.');
  }
  const numbers = associated.map(pr => pr?.number);
  if (!numbers.every(positiveInteger)) throw Error('Invalid PR association evidence.');
  const matches = [];
  for (const number of new Set(numbers)) {
    const pr = await client(`${base}/pulls/${number}`);
    if (pr?.number === number && pr.merged === true && pr.merged_at
        && pr.merge_commit_sha === sha && pr.base?.ref === policy.branch
        && pr.base?.repo?.full_name === policy.repository && policy.mergerIds.includes(pr.merged_by?.id)) {
      matches.push({ pullRequest: number, mergerId: pr.merged_by.id });
    }
  }
  if (matches.length !== 1) throw Error('The exact release commit must come from one PR merged by an allowed numeric owner identity.');
  return matches[0];
}

export async function verifyMergedRelease(context, policy, request) {
  const admission = mergePolicy(policy);
  const { repository, ref, sha, event } = context ?? {};
  if (repository !== admission.repository || ref !== `refs/heads/${admission.branch}`
      || typeof sha !== 'string' || !/^[a-f0-9]{40}$/.test(sha) || !admission.events.includes(event)) {
    throw Error(`Production requires an accepted ${admission.branch} event in ${admission.repository}.`);
  }
  if (typeof request !== 'function') throw Error('A GitHub evidence client is required.');
  await requireCurrentBranch(request, admission, sha);
  const match = await ownerMerge(request, admission, sha);
  await requireCurrentBranch(request, admission, sha, true);
  return { repository, sha, ...match };
}

// total_count, full pages and unique identities are all required. A short, repeated,
// changing or oversized result is incomplete evidence, even if a green gate is visible.
async function collection(client, path, field, perPage, label, explicitFirstPage = false) {
  const items = [];
  const ids = new Set();
  let total;
  for (let page = 1; page <= Math.ceil(MAX_EVIDENCE_ITEMS / perPage); page++) {
    const response = await client(page === 1 && !explicitFirstPage ? path : `${path}&page=${page}`);
    const count = response?.total_count;
    const entries = response?.[field];
    if (!Number.isSafeInteger(count) || count < 0 || count > MAX_EVIDENCE_ITEMS
        || (total !== undefined && count !== total) || !Array.isArray(entries)
        || entries.length !== Math.min(perPage, count - items.length)) {
      throw Error(`Complete, bounded ${label} pagination is required.`);
    }
    total = count;
    for (const entry of entries) {
      if (!positiveInteger(entry?.id) || ids.has(entry.id)) throw Error(`Invalid or duplicate ${label} pagination identity.`);
      ids.add(entry.id);
      items.push(entry);
    }
    if (items.length === total) return items;
  }
  throw Error(`Complete, bounded ${label} pagination is required.`);
}

function seconds(value, fallback, name, minimum = 0) {
  const parsed = value ?? fallback;
  if (!Number.isSafeInteger(parsed) || parsed < minimum || parsed > Number.MAX_SAFE_INTEGER / 1000) {
    throw Error(`${name} must be a ${minimum ? 'positive' : 'non-negative'} integer.`);
  }
  return parsed;
}

export function createGitHubReleaseGate(policy) {
  const admission = mergePolicy(policy);
  if (typeof policy.workflow !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9_.-]*\.ya?ml$/.test(policy.workflow)
      || policy.workflow.includes('..') || !nonemptyStrings(policy.requiredJobs)) {
    throw Error('An explicit canonical workflow filename and unique required jobs policy is required.');
  }
  const workflow = policy.workflow;
  const requiredJobs = [...policy.requiredJobs];
  const workflowPath = `.github/workflows/${workflow}`;

  function requireCanonicalRun(run) {
    if (run?.path !== workflowPath) {
      throw Error(`Workflow run ${run?.id ?? 'unknown'} is not the canonical CI workflow ${workflowPath}.`);
    }
    if (!positiveInteger(run.id) || !positiveInteger(run.run_attempt)) {
      throw Error('Complete CI run identity and run attempt evidence is required.');
    }
  }

  function validateEvidence({ expectedSha, mainSha, run, jobs }) {
    const sha = requireSha(expectedSha);
    if (requireSha(mainSha) !== sha) throw Error(`Validated SHA ${sha} is stale; current ${admission.branch} is ${mainSha}.`);
    if (!run) throw Error(`No CI run exists for ${admission.branch} @ ${sha}.`);
    requireCanonicalRun(run);
    if (run.head_sha !== sha) throw Error(`CI run ${run.id} reviewed ${run.head_sha || 'no SHA'}, expected ${sha}.`);
    if (run.head_branch !== admission.branch) throw Error(`CI run ${run.id} is for branch ${run.head_branch || 'unknown'}, not ${admission.branch}.`);
    if (!admission.events.includes(run.event)) throw Error(`CI run ${run.id} was triggered by ${run.event || 'unknown'}, not an accepted ${admission.branch} CI trigger.`);
    if (run.status !== 'completed' || run.conclusion !== 'success') {
      throw Error(`CI run ${run.id} is not green: status=${run.status || 'unknown'}, conclusion=${run.conclusion || 'unknown'}.`);
    }
    if (!Array.isArray(jobs)) throw Error('Complete CI job evidence is required.');
    const gates = requiredJobs.map(name => {
      const matches = jobs.filter(job => job?.name === name);
      if (matches.length !== 1) throw Error(`Expected exactly one ${name} job, found ${matches.length}.`);
      return matches[0];
    });
    const ids = new Set();
    for (const job of jobs) {
      if (!positiveInteger(job?.id) || ids.has(job.id)) throw Error('Invalid or duplicate CI job identity.');
      ids.add(job.id);
      if (job.run_id !== run.id || job.run_attempt !== run.run_attempt || job.head_sha !== sha) {
        throw Error(`Job ${job.id} does not belong to CI run ${run.id}, attempt ${run.run_attempt}, SHA ${sha}.`);
      }
    }
    for (const gate of gates) {
      if (gate.status !== 'completed' || gate.conclusion !== 'success') {
        throw Error(`${gate.name} is not green: status=${gate.status || 'unknown'}, conclusion=${gate.conclusion || 'unknown'}.`);
      }
    }
    // Preserve the existing single-gate receipt; every declared gate was checked.
    return { sha, runId: run.id, runUrl: run.html_url || '', gateId: gates[0].id, gateUrl: gates[0].html_url || '' };
  }

  function latestRun(runs, expectedSha) {
    const sha = requireSha(expectedSha);
    if (!Array.isArray(runs)) throw Error('Complete CI workflow run evidence is required.');
    const matching = runs.filter(run => run?.head_sha === sha && run.head_branch === admission.branch
      && admission.events.includes(run.event) && run.path === workflowPath);
    for (const run of matching) requireCanonicalRun(run);
    // Run IDs increase for new GitHub runs. A rerun keeps its ID and raises its attempt.
    matching.sort((a, b) => b.id - a.id || b.run_attempt - a.run_attempt);
    return matching[0] ?? null;
  }

  function chooseRun(runs, expectedSha) {
    const run = latestRun(runs, expectedSha);
    if (!run) return { state: 'missing', run: null };
    if (run.status === 'completed' && run.conclusion === 'success') return { state: 'success', run };
    if (ACTIVE_RUN_STATES.has(run.status)) return { state: 'pending', run: null };
    return { state: 'failed', run };
  }

  function workflowRunsPath(sha) {
    const query = new URLSearchParams({ branch: admission.branch, head_sha: requireSha(sha), per_page: '20' });
    return `/actions/workflows/${workflow}/runs?${query}`;
  }

  async function verifyOwnerMerge(client, repository, sha) {
    requireRepository(repository, admission);
    requireSha(sha);
    return (await ownerMerge(client, admission, sha)).pullRequest;
  }

  async function discover(client, repository, sha) {
    const runs = await collection(client, `/repos/${repository}${workflowRunsPath(sha)}`, 'workflow_runs', 20, 'workflow run');
    return chooseRun(runs, sha);
  }

  async function loadRun(client, repository, runId) {
    const run = await client(`/repos/${repository}/actions/runs/${runId}`);
    requireCanonicalRun(run);
    if (run.id !== runId) throw Error('The CI run response does not match the requested run identity.');
    return run;
  }

  async function resolveEvidence({ client, repository, sha, ciRunId = 0, waitSeconds = 0, pollSeconds = 45 }) {
    requireRepository(repository, admission);
    requireSha(sha);
    if (typeof client !== 'function') throw Error('A GitHub evidence client is required.');
    if (!Number.isSafeInteger(ciRunId) || ciRunId < 0) throw Error('ciRunId must be a non-negative integer.');
    const deadline = Date.now() + seconds(waitSeconds, 0, 'waitSeconds') * 1000;
    const poll = seconds(pollSeconds, 45, 'pollSeconds', 1) * 1000;
    while (true) {
      await requireCurrentBranch(client, admission, sha);
      await verifyOwnerMerge(client, repository, sha);
      const discovered = await discover(client, repository, sha);
      if (discovered.state === 'success') {
        if (ciRunId && discovered.run.id !== ciRunId) throw Error('The requested CI run is not the latest canonical run for this SHA.');
        const run = await loadRun(client, repository, discovered.run.id);
        if (run.run_attempt !== discovered.run.run_attempt) throw Error('The selected CI run attempt changed during verification.');
        const jobs = await collection(client,
          `/repos/${repository}/actions/runs/${run.id}/attempts/${run.run_attempt}/jobs?per_page=${PAGE_SIZE}`,
          'jobs', PAGE_SIZE, 'job', true);
        validateEvidence({ expectedSha: sha, mainSha: sha, run, jobs });
        const currentRun = await loadRun(client, repository, run.id);
        const evidence = validateEvidence({ expectedSha: sha, mainSha: sha, run: currentRun, jobs });
        const latest = await discover(client, repository, sha);
        if (latest.state !== 'success' || latest.run.id !== run.id || latest.run.run_attempt !== run.run_attempt) {
          throw Error('The latest canonical CI run or attempt changed during evidence collection.');
        }
        await requireCurrentBranch(client, admission, sha, true);
        return evidence;
      }
      if (discovered.state === 'failed') {
        throw Error(`CI for ${admission.branch} @ ${sha} completed without success (conclusion=${discovered.run.conclusion || 'unknown'}).`);
      }
      const remaining = deadline - Date.now();
      if (remaining <= 0) throw Error(`Timed out waiting for successful ${requiredJobs.join(', ')} for ${admission.branch} @ ${sha}.`);
      console.log(`[release] CI for ${sha.slice(0, 12)} is ${discovered.state}; waiting ${Math.min(poll, remaining) / 1000}s.`);
      await new Promise(resolve => setTimeout(resolve, Math.min(poll, remaining, 60_000)));
    }
  }

  return { validateEvidence, chooseRun, workflowRunsPath, verifyOwnerMerge, resolveEvidence };
}
