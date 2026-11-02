import fs from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createGitHubReleaseGate } from './boney/github.mjs';
import { loadProfile } from './boney/profile.mjs';
import { verifySnapshot } from './boney/snapshot.mjs';
import { githubClient } from './boney/cli.mjs';

// The committed snapshot is an explicit Boney upgrade, checked before release use.
await verifySnapshot(fileURLToPath(new URL('./boney/', import.meta.url)));
export const deliveryProfile = await loadProfile(new URL('./product.delivery.json', import.meta.url));
const gate = createGitHubReleaseGate({ ...deliveryProfile, ...deliveryProfile.ci });
export const { validateEvidence, chooseRun, workflowRunsPath, verifyOwnerMerge, resolveEvidence } = gate;

function parseArgs(argv) {
  const values = new Map();
  for (let index = 0; index < argv.length; index += 2) {
    const arg = argv[index], value = argv[index + 1];
    if (!['--repository', '--sha', '--ci-run-id', '--wait-seconds', '--poll-seconds'].includes(arg)
        || !value || value.startsWith('--') || values.has(arg.slice(2))) throw Error('Unknown or incomplete production eligibility argument.');
    values.set(arg.slice(2), value);
  }
  return values;
}

function integer(value, fallback, name) {
  if (value == null) return fallback;
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < 0 || parsed > 3600) throw Error(`${name} must be a bounded non-negative integer.`);
  return parsed;
}

function publishEvidence(evidence) {
  console.log(`[release] production eligible: main @ ${evidence.sha}; CI run ${evidence.runId}; CI Gate ${evidence.gateId}.`);
  if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `validated_sha=${evidence.sha}\nci_run_id=${evidence.runId}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY,
    `\n### Production eligibility\n\n- SHA: \`${evidence.sha}\`\n- CI run: ${evidence.runId}\n- CI Gate: ${evidence.gateId} — success\n`);
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const sha = args.get('sha') || process.env.GITHUB_SHA || '';
  if (!/^[0-9a-f]{40}$/.test(sha)) throw Error('Production eligibility requires an exact 40-character commit SHA.');
  const repository = args.get('repository') || process.env.GITHUB_REPOSITORY;
  if (repository !== deliveryProfile.repository) throw Error('Production ownership requires the declared Workslip repository.');
  const runId = args.get('ci-run-id');
  if (runId !== undefined && (!/^[1-9][0-9]*$/.test(runId) || !Number.isSafeInteger(Number(runId)))) throw Error('ci-run-id must be a positive integer.');
  const evidence = await resolveEvidence({
    client: githubClient(), repository, sha, ciRunId: runId ? Number(runId) : 0,
    waitSeconds: integer(args.get('wait-seconds'), 0, 'wait-seconds'),
    pollSeconds: Math.max(5, integer(args.get('poll-seconds'), 45, 'poll-seconds')),
  });
  publishEvidence(evidence);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => { console.error(`[release] production blocked: ${error.message}`); process.exitCode = 1; });
}
