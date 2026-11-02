import fs from 'node:fs';
import { spawn, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const runtimeFiles = [
  'Boney.Delivery.csproj', 'Json.cs', 'DeliveryProfile.cs', 'GitHubReleaseGate.cs',
  'ReleasePipeline.cs', 'FileReleaseStore.cs', 'ReleaseRisk.cs', 'TrackedSources.cs',
  'DeliverySnapshot.cs', 'DeliveryScaffold.cs', 'ReferenceRelease.cs',
  'DeliveryCommandLine.cs', 'CSharpProcessAdapter.cs', 'Program.cs', 'DeliveryPolicyProtocol.cs',
];
const maxMessage = 4 * 1024 * 1024;

// This reviewed entrypoint and lock are the trust root. Check every source and
// project byte before MSBuild can run, then build only those bytes in a fresh
// directory. Existing bin/obj outputs are never executable input.
function verifiedSources(directory) {
  try {
    const lockPath = join(directory, 'boney.lock.json');
    if (!fs.lstatSync(lockPath).isFile()) throw Error();
    const lockBytes = fs.readFileSync(lockPath), lock = JSON.parse(lockBytes);
    const expected = [...runtimeFiles].sort();
    if (lock?.schemaVersion !== 1 || lock.sourceRepository !== 'TBD-APS/boney'
        || !/^[a-f0-9]{40}$/.test(lock.sourceRevision ?? '')
        || JSON.stringify(Object.keys(lock.files ?? {}).sort()) !== JSON.stringify(expected)) throw Error();
    const inventory = fs.readdirSync(directory).filter(name => {
      if (!['bin', 'obj'].includes(name)) return true;
      if (!fs.lstatSync(join(directory, name)).isDirectory()) throw Error();
      return false;
    }).sort();
    if (JSON.stringify(inventory) !== JSON.stringify([...expected, 'boney.lock.json'].sort())) throw Error();
    const bytes = new Map([['boney.lock.json', lockBytes]]);
    for (const name of runtimeFiles) {
      if (!fs.lstatSync(join(directory, name)).isFile() || !/^[a-f0-9]{64}$/.test(lock.files[name] ?? '')) throw Error();
      const source = fs.readFileSync(join(directory, name));
      if (createHash('sha256').update(source).digest('hex') !== lock.files[name]) throw Error();
      bytes.set(name, source);
    }
    return bytes;
  } catch { throw Error('DELIVERY_SNAPSHOT_INVALID'); }
}

const snapshotDirectory = fileURLToPath(new URL('./boney/', import.meta.url));
const sources = verifiedSources(snapshotDirectory);
let assembly;
function build() {
  const current = verifiedSources(snapshotDirectory);
  if (!current.get('boney.lock.json').equals(sources.get('boney.lock.json'))) throw Error('DELIVERY_SNAPSHOT_INVALID');
  if (assembly) return assembly;
  const temporary = fs.mkdtempSync(join(tmpdir(), 'workslip-boney-build-'));
  const directory = join(temporary, 'source'); fs.mkdirSync(directory);
  process.once('exit', () => fs.rmSync(temporary, { recursive: true, force: true }));
  for (const [name, bytes] of sources) fs.writeFileSync(join(directory, name), bytes, { flag: 'wx' });
  const nuget = join(temporary, 'NuGet.Config');
  fs.writeFileSync(nuget, '<configuration><packageSources><clear /></packageSources></configuration>');
  const result = spawnSync('dotnet', ['build', join(directory, 'Boney.Delivery.csproj'), '--configuration', 'Release', '--nologo', '--verbosity', 'quiet',
    '-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false', '-p:ImportDirectoryPackagesProps=false',
    '-p:NuGetAudit=false', `-p:RestoreConfigFile=${nuget}`],
    { encoding: 'utf8', timeout: 120000, maxBuffer: maxMessage });
  if (result.error || result.status !== 0) throw Error('DELIVERY_BUILD_FAILED');
  assembly = join(directory, 'bin', 'Release', 'net10.0', 'Boney.Delivery.dll');
  return assembly;
}

function policy(operation, input = {}) {
  const result = spawnSync('dotnet', [build(), 'policy'], {
    input: JSON.stringify({ operation, ...input }) + '\n', encoding: 'utf8', timeout: 30000, maxBuffer: maxMessage,
  });
  if (result.error) throw Error('DELIVERY_PROTOCOL_FAILED');
  let response;
  try { response = JSON.parse(result.stdout); } catch { throw Error('DELIVERY_PROTOCOL_FAILED'); }
  if (response.error) throw Error(response.error);
  if (result.status !== 0 || !Object.hasOwn(response, 'result')) throw Error('DELIVERY_PROTOCOL_FAILED');
  return response.result;
}

function requestPolicy(operation, input, request) {
  return new Promise((accept, reject) => {
    const child = spawn('dotnet', [build(), 'policy'], { stdio: ['pipe', 'pipe', 'pipe'] });
    let buffer = '', response, requestId = 0, pending = false, failure;
    const refuse = () => { failure ??= Error('DELIVERY_PROTOCOL_FAILED'); child.kill(); };
    // C# owns the CI wait deadline. Bound transport inactivity, allowing a full
    // pagination pass to make progress across many individually bounded GETs.
    const timer = setTimeout(refuse, 120000);
    child.stderr.resume();
    child.stdin.on('error', refuse);
    child.on('error', refuse);
    child.stdout.setEncoding('utf8');
    child.stdout.on('data', chunk => {
      timer.refresh();
      buffer += chunk;
      if (Buffer.byteLength(buffer) > maxMessage) return refuse();
      let newline;
      while ((newline = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, newline); buffer = buffer.slice(newline + 1);
        let message;
        try {
          message = JSON.parse(line);
          if (!message || typeof message !== 'object' || Array.isArray(message)) throw Error();
        } catch { refuse(); return; }
        if (response || pending) { refuse(); return; }
        if (Object.hasOwn(message, 'request')) {
          if (message.request !== ++requestId || typeof message.path !== 'string'
              || !message.path.startsWith(`/repos/${input.profile.repository}/`) || message.path.includes('..')
              || /[\r\n]/.test(message.path)) { refuse(); return; }
          pending = true;
          Promise.resolve().then(() => request(message.path)).then(value => {
            const answer = JSON.stringify({ request: requestId, value });
            if (Buffer.byteLength(answer) > maxMessage) { refuse(); return; }
            pending = false; timer.refresh(); child.stdin.write(answer + '\n');
          }).catch(() => { pending = false; timer.refresh(); child.stdin.write(JSON.stringify({ request: requestId, error: true }) + '\n'); });
        } else {
          response = message; child.stdin.end();
        }
      }
    });
    child.on('close', code => {
      clearTimeout(timer);
      if (failure || pending || buffer.trim() || !response) return reject(failure ?? Error('DELIVERY_PROTOCOL_FAILED'));
      if (response.error) return reject(Error(response.error));
      if (code !== 0 || !Object.hasOwn(response, 'result')) return reject(Error('DELIVERY_PROTOCOL_FAILED'));
      accept(response.result);
    });
    child.stdin.write(JSON.stringify({ operation, ...input }) + '\n');
  });
}

function githubClient(token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN) {
  if (!token) throw Error('GITHUB_READ_TOKEN_REQUIRED');
  return async path => {
    const response = await fetch(`https://api.github.com${path}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' }, redirect: 'error', signal: AbortSignal.timeout(15000) });
    if (!response.ok) throw Error('GITHUB_EVIDENCE_UNAVAILABLE');
    const text = await response.text();
    if (Buffer.byteLength(text) > maxMessage) throw Error('GITHUB_EVIDENCE_TOO_LARGE');
    return JSON.parse(text);
  };
}

export const deliveryProfile = policy('validate-profile', { profile: JSON.parse(fs.readFileSync(new URL('./product.delivery.json', import.meta.url), 'utf8')) });
export const validateEvidence = ({ expectedSha, mainSha, run, jobs }) => policy('validate-evidence', { profile: deliveryProfile, sha: expectedSha, mainSha, run, jobs });
export const chooseRun = (runs, sha) => policy('choose-run', { profile: deliveryProfile, runs, sha });
export const workflowRunsPath = sha => policy('workflow-runs-path', { profile: deliveryProfile, sha });
export async function verifyOwnerMerge(client, repository, sha) {
  if (repository !== deliveryProfile.repository) throw Error('RELEASE_CONTEXT_INVALID');
  return (await requestPolicy('verify-owner-merge', { profile: deliveryProfile, sha }, client)).pullRequest;
}
export async function resolveEvidence({ client, repository, sha, ciRunId = 0, waitSeconds = 0, pollSeconds = 45 }) {
  if (repository !== deliveryProfile.repository) throw Error('RELEASE_CONTEXT_INVALID');
  return requestPolicy('resolve-evidence', { profile: deliveryProfile, sha, ciRunId, waitSeconds, pollSeconds }, client);
}

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
