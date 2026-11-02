import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { cp, mkdtemp, readFile, writeFile, rm, mkdir, chmod } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chooseRun, validateEvidence, workflowRunsPath, verifyOwnerMerge, resolveEvidence, deliveryProfile } from './verify-production-eligibility.mjs';

const SHA = 'a'.repeat(40);
const OTHER_SHA = 'b'.repeat(40);
const SCRIPT = fileURLToPath(new URL('./verify-production-eligibility.mjs', import.meta.url));

function greenRun(overrides = {}) {
  return {
    id: 101,
    run_attempt: 1,
    name: 'CI',
    path: '.github/workflows/frontend-validation.yml',
    head_sha: SHA,
    head_branch: 'main',
    event: 'push',
    status: 'completed',
    conclusion: 'success',
    html_url: 'https://example.test/run/101',
    ...overrides,
  };
}

function greenGate(overrides = {}) {
  return {
    id: 202,
    run_id: 101,
    run_attempt: 1,
    head_sha: SHA,
    name: 'CI Gate',
    status: 'completed',
    conclusion: 'success',
    html_url: 'https://example.test/job/202',
    ...overrides,
  };
}

test('accepts only an exact successful main CI Gate', () => {
  const evidence = validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [greenGate()] });
  assert.equal(evidence.sha, SHA);
  assert.equal(evidence.runId, 101);
});

test('accepts a successful manually dispatched CI run on the exact current main SHA', () => {
  const evidence = validateEvidence({
    expectedSha: SHA,
    mainSha: SHA,
    run: greenRun({ event: 'workflow_dispatch' }),
    jobs: [greenGate()],
  });
  assert.equal(evidence.runId, 101);
});

test('rejects stale successful CI when main has advanced', () => {
  assert.throws(
    () => validateEvidence({ expectedSha: SHA, mainSha: OTHER_SHA, run: greenRun(), jobs: [greenGate()] }),
    /RELEASE_SOURCE_STALE/,
  );
});

test('rejects every non-success workflow conclusion', () => {
  for (const conclusion of ['failure', 'cancelled', 'timed_out', 'action_required', 'neutral', 'skipped', null]) {
    assert.throws(
      () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun({ conclusion }), jobs: [greenGate()] }),
      /CI_RUN_NOT_GREEN/,
    );
  }
});

test('rejects a wrong branch and unapproved CI trigger', () => {
  assert.throws(
    () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun({ head_branch: 'feature' }), jobs: [greenGate()] }),
    /CI_RUN_NOT_GREEN/,
  );
  assert.throws(
    () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun({ event: 'schedule' }), jobs: [greenGate()] }),
    /CI_RUN_NOT_GREEN/,
  );
});

test('rejects missing, duplicate, skipped or red CI Gate', () => {
  assert.throws(() => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [] }), /CI_REQUIRED_JOB_NOT_GREEN/);
  assert.throws(
    () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [greenGate(), greenGate({ id: 203 })] }),
    /CI_REQUIRED_JOB_NOT_GREEN/,
  );
  for (const conclusion of ['failure', 'cancelled', 'skipped', null]) {
    assert.throws(
      () => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [greenGate({ conclusion })] }),
      /CI_REQUIRED_JOB_NOT_GREEN/,
    );
  }
});

test('run discovery prefers success, waits for active CI and fails closed otherwise', () => {
  assert.equal(chooseRun([greenRun()], SHA).state, 'success');
  assert.equal(chooseRun([greenRun({ event: 'workflow_dispatch' })], SHA).state, 'success');
  assert.equal(chooseRun([greenRun({ event: 'schedule' })], SHA).state, 'missing');
  assert.equal(chooseRun([greenRun({ status: 'in_progress', conclusion: null })], SHA).state, 'pending');
  assert.equal(chooseRun([greenRun({ conclusion: 'failure' })], SHA).state, 'failed');
  assert.equal(chooseRun([], SHA).state, 'missing');
});

test('run discovery does not exclude manually dispatched CI evidence at the API boundary', () => {
  assert.equal(
    workflowRunsPath(SHA),
    `/actions/workflows/frontend-validation.yml/runs?branch=main&head_sha=${SHA}&per_page=20`,
  );
});

test('CLI entrypoint executes and fails closed on invalid SHA', () => {
  const result = spawnSync(process.execPath, [SCRIPT, '--repository', 'owner/repo', '--sha', 'not-a-sha'], {
    encoding: 'utf8',
    env: { ...process.env, GITHUB_TOKEN: '' },
  });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /production blocked: Production eligibility requires an exact 40-character commit SHA/);
});

const REPOSITORY = 'TBD-APS/Workslip';
function ownerPull() { return { number: 12, merged: true, merged_at: '2026-10-26T12:00:00Z', merge_commit_sha: SHA, merged_by: { id: 31623093 }, base: { ref: 'main', repo: { full_name: REPOSITORY } } }; }
function ownerApi(pr = ownerPull(), associated = [{ number: 12 }]) { return async path => path.includes('/commits/') ? associated : pr; }
test('requires one exact main PR merged by the immutable owner identity', async () => {
  assert.equal(await verifyOwnerMerge(ownerApi(), REPOSITORY, SHA), 12);
  for (const mutate of [p => p.merged_by.id = 1, p => p.merged_by = { login: 'rasm105k' }, p => p.merged = false, p => p.merge_commit_sha = OTHER_SHA, p => p.base.ref = 'feature', p => p.base.repo.full_name = 'foreign/repo', p => p.number = 13]) {
    const pr = ownerPull(); mutate(pr);
    await assert.rejects(verifyOwnerMerge(ownerApi(pr), REPOSITORY, SHA));
  }
});
test('large UTF-8 pull-request metadata completes over the open callback channel', async () => {
  const originalTimeout = globalThis.setTimeout;
  globalThis.setTimeout = (callback, delay, ...args) => originalTimeout(callback, delay >= 60000 ? 2000 : delay, ...args);
  try {
    const associations = [{ number: 12, title: 'Release — verified', body: 'x'.repeat(5000) }];
    assert.equal(await verifyOwnerMerge(ownerApi(ownerPull(), associations), REPOSITORY, SHA), 12);
  } finally { globalThis.setTimeout = originalTimeout; }
});
test('denies direct pushes, incomplete evidence, wrong repositories and provider failures', async () => {
  for (const associations of [[], null, Array(100).fill({ number: 12 }), [{ number: '../12' }]]) {
    await assert.rejects(verifyOwnerMerge(ownerApi(ownerPull(), associations), REPOSITORY, SHA));
  }
  await assert.rejects(verifyOwnerMerge(ownerApi(), 'foreign/repo', SHA));
  await assert.rejects(verifyOwnerMerge(async () => { throw Error('unavailable'); }, REPOSITORY, SHA));
});

test('the Workslip adapter collects canonical attempt-bound evidence through the pinned Boney implementation', async () => {
  const paths = [];
  const client = async path => {
    paths.push(path);
    if (path.endsWith('/git/ref/heads/main')) return { object: { sha: SHA } };
    if (path.includes('/commits/')) return [{ number: 12 }];
    if (path.endsWith('/pulls/12')) return ownerPull();
    if (path.includes('/workflows/')) return { total_count: 1, workflow_runs: [greenRun()] };
    if (path.endsWith('/actions/runs/101')) return greenRun();
    if (path.includes('/attempts/1/jobs')) return { total_count: 1, jobs: [greenGate()] };
    throw Error('Unexpected GitHub request');
  };
  const result = await resolveEvidence({ client, repository: REPOSITORY, sha: SHA });
  assert.equal(result.sha, SHA); assert.equal(result.gateId, 202);
  assert.deepEqual(deliveryProfile.ci.requiredJobs, ['CI Gate']);
  assert.ok(paths.some(path => path.includes('/attempts/1/jobs')));
  assert.equal(paths.filter(path => path.endsWith('/git/ref/heads/main')).length, 2);
});

test('continued GitHub evidence progress does not expire the transport watchdog', async () => {
  const originalTimeout = globalThis.setTimeout;
  // Scale only the long watchdog down: the complete pass exceeds its timeout,
  // while each successful request makes progress before inactivity can expire.
  globalThis.setTimeout = (callback, delay, ...args) => originalTimeout(callback, delay >= 60000 ? 400 : delay, ...args);
  let requests = 0;
  try {
    const client = async path => {
      requests++;
      await new Promise(done => originalTimeout(done, 80));
      if (path.endsWith('/git/ref/heads/main')) return { object: { sha: SHA } };
      if (path.includes('/commits/')) return [{ number: 12 }];
      if (path.endsWith('/pulls/12')) return ownerPull();
      if (path.includes('/workflows/')) return { total_count: 1, workflow_runs: [greenRun()] };
      if (path.endsWith('/actions/runs/101')) return greenRun();
      if (path.includes('/attempts/1/jobs')) return { total_count: 1, jobs: [greenGate()] };
      throw Error('Unexpected GitHub request');
    };
    const result = await resolveEvidence({ client, repository: REPOSITORY, sha: SHA });
    assert.equal(result.gateId, 202);
    assert.ok(requests >= 9);
  } finally { globalThis.setTimeout = originalTimeout; }
});

test('a newer failed canonical run prevents an older green result from authorizing production', () => {
  assert.equal(chooseRun([greenRun({ id: 102, conclusion: 'failure' }), greenRun()], SHA).state, 'failed');
  assert.throws(() => validateEvidence({ expectedSha: SHA, mainSha: SHA, run: greenRun(), jobs: [greenGate({ run_attempt: 2 })] }), /CI_JOB_IDENTITY_INVALID/);
});

test('a modified Boney snapshot blocks the actual Workslip CLI before provider admission', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'workslip-delivery-')); t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(new URL('./boney/', import.meta.url), join(directory, 'boney'), { recursive: true });
  await cp(SCRIPT, join(directory, 'verify-production-eligibility.mjs'));
  await cp(new URL('./product.delivery.json', import.meta.url), join(directory, 'product.delivery.json'));
  const modified = join(directory, 'boney', 'ReleasePipeline.cs');
  await writeFile(modified, (await readFile(modified, 'utf8')) + '\n// unexpected drift\n');
  const result = spawnSync(process.execPath, [join(directory, 'verify-production-eligibility.mjs'), '--sha', SHA, '--repository', REPOSITORY], { encoding: 'utf8', env: { ...process.env, GITHUB_TOKEN: '' } });
  assert.notEqual(result.status, 0); assert.match(result.stderr, /DELIVERY_SNAPSHOT_INVALID/);
  const marker = join(directory, 'executed-unverified-code');
  const engine = join(directory, 'boney', 'Program.cs');
  await writeFile(engine, (await readFile(engine, 'utf8')) + `\nSystem.IO.File.WriteAllText(${JSON.stringify(marker)}, "executed");\n`);
  const malicious = spawnSync(process.execPath, [join(directory, 'verify-production-eligibility.mjs'), '--sha', SHA, '--repository', REPOSITORY], { encoding: 'utf8', env: { ...process.env, GITHUB_TOKEN: '' } });
  assert.notEqual(malicious.status, 0); assert.match(malicious.stderr, /DELIVERY_SNAPSHOT_INVALID/);
  await assert.rejects(readFile(marker), { code: 'ENOENT' });
});

test('an altered C# build project cannot execute before the Workslip entrypoint checks its bytes', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'workslip-bootstrap-')); t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(new URL('./boney/', import.meta.url), join(directory, 'boney'), { recursive: true });
  await cp(SCRIPT, join(directory, 'verify-production-eligibility.mjs'));
  await cp(new URL('./product.delivery.json', import.meta.url), join(directory, 'product.delivery.json'));
  const marker = join(directory, 'unverified-bootstrap');
  const verifier = join(directory, 'boney', 'Boney.Delivery.csproj');
  await writeFile(verifier, (await readFile(verifier, 'utf8')).replace('</Project>', `<Target Name="Unreviewed" BeforeTargets="BeforeBuild"><WriteLinesToFile File="${marker}" Lines="executed" /></Target></Project>`));
  const fakeBin = join(directory, 'fake-bin'); await mkdir(fakeBin);
  const compiler = join(fakeBin, 'dotnet');
  await writeFile(compiler, `#!${process.execPath}\nrequire('node:fs').writeFileSync(${JSON.stringify(marker)}, 'compiler invoked'); process.exit(91);\n`);
  await chmod(compiler, 0o755);
  for (const args of [
    [join(directory, 'verify-production-eligibility.mjs'), '--sha', SHA, '--repository', REPOSITORY],
    ['--input-type=module', '-e', `await import(${JSON.stringify(pathToFileURL(join(directory, 'verify-production-eligibility.mjs')).href)})`],
  ]) {
    const result = spawnSync(process.execPath, args, { encoding: 'utf8', env: { ...process.env, GITHUB_TOKEN: '', PATH: `${fakeBin}${process.platform === 'win32' ? ';' : ':'}${process.env.PATH}` } });
    assert.notEqual(result.status, 0); assert.match(result.stderr, /DELIVERY_SNAPSHOT_INVALID/);
    await assert.rejects(readFile(marker), { code: 'ENOENT' });
  }
});

test('unverified cached build outputs cannot supply the Workslip policy executable', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'workslip-cached-output-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(new URL('./boney/', import.meta.url), join(directory, 'boney'), { recursive: true });
  await cp(SCRIPT, join(directory, 'verify-production-eligibility.mjs'));
  await cp(new URL('./product.delivery.json', import.meta.url), join(directory, 'product.delivery.json'));
  const output = join(directory, 'boney', 'bin', 'Release', 'net10.0');
  await mkdir(output, { recursive: true });
  await writeFile(join(output, 'Boney.Delivery.dll'), 'unverified cached executable');
  await mkdir(join(directory, 'boney', 'obj'), { recursive: true });
  await writeFile(join(directory, 'boney', 'obj', 'project.assets.json'), 'unverified cached compiler input');
  const result = spawnSync(process.execPath, ['--input-type=module', '-e',
    `const gate = await import(${JSON.stringify(pathToFileURL(join(directory, 'verify-production-eligibility.mjs')).href)}); console.log(gate.chooseRun([], ${JSON.stringify(SHA)}).state);`],
  { encoding: 'utf8', env: { ...process.env, GITHUB_TOKEN: '' }, timeout: 120000 });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim(), 'missing');
});
