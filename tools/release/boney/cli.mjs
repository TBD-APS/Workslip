import { mkdir, writeFile, realpath } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { verifySnapshot } from './snapshot.mjs';

export function githubClient(token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN) {
  if (!token) throw Error('GITHUB_READ_TOKEN_REQUIRED');
  return async path => {
    if (!path.startsWith('/repos/') || path.includes('..') || /[\r\n]/.test(path)) throw Error('GITHUB_EVIDENCE_PATH_INVALID');
    const response = await fetch(`https://api.github.com${path}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' }, redirect: 'error', signal: AbortSignal.timeout(15000) });
    if (!response.ok) throw Error(`GITHUB_EVIDENCE_HTTP_${response.status}`);
    const text = await response.text();
    if (text.length > 4 * 1024 * 1024) throw Error('GITHUB_EVIDENCE_TOO_LARGE');
    return JSON.parse(text);
  };
}

async function main(args) {
  const [command, ...rest] = args;
  if (command === 'verify-snapshot' && rest.length === 1) { console.log(JSON.stringify(await verifySnapshot(resolve(rest[0])))); return; }
  // Every operational command uses a complete exported snapshot. Do not load
  // engine modules until its inventory and bytes have been verified.
  await verifySnapshot(dirname(fileURLToPath(import.meta.url)));
  const { loadProfile } = await import('./profile.mjs');
  if (command === 'validate-profile' && rest.length === 1) { const profile = await loadProfile(resolve(rest[0])); console.log(JSON.stringify({ product: profile.product, schemaVersion: profile.schemaVersion })); return; }
  if (!['run', 'admit'].includes(command) || rest.length % 2) throw Error('DELIVERY_USAGE');
  const options = {};
  for (let index = 0; index < rest.length; index += 2) {
    const key = rest[index];
    if (!['--profile', '--adapter', '--environment', '--revision', '--store', '--receipt'].includes(key) || Object.hasOwn(options, key) || !rest[index + 1]) throw Error('DELIVERY_USAGE');
    options[key] = rest[index + 1];
  }
  if (!options['--profile'] || !options['--revision']) throw Error('DELIVERY_USAGE');
  if (command === 'run' && (!options['--adapter'] || !options['--environment'] || !options['--receipt'])) throw Error('DELIVERY_USAGE');
  const { requireTrackedSources } = await import('./source.mjs');
  const paths = [options['--profile'], ...(command === 'run' ? [options['--adapter']] : [])];
  const [profilePath, adapterPath] = await requireTrackedSources({ revision: options['--revision'], paths });
  const profile = await loadProfile(profilePath);
  const context = { repository: process.env.GITHUB_REPOSITORY, ref: process.env.GITHUB_REF, event: process.env.GITHUB_EVENT_NAME, sha: options['--revision'] };
  const request = githubClient();
  const { executeRelease, authorizeRelease } = await import('./pipeline.mjs');
  if (command === 'admit') { console.log(JSON.stringify(await authorizeRelease(profile, context, request))); return; }
  // Admission precedes loading executable product code.
  await authorizeRelease(profile, context, request);
  await requireTrackedSources({ revision: context.sha, paths });
  const adapter = await import(pathToFileURL(adapterPath).href);
  const { FileReleaseStore } = await import('./store.mjs');
  const store = options['--store'] ? new FileReleaseStore(options['--store'])
    : typeof adapter.createStore === 'function' ? await adapter.createStore({ profile, environment: options['--environment'], context }) : null;
  if (!store || typeof store.withLock !== 'function') throw Error('DURABLE_RELEASE_STORE_REQUIRED');
  const receipt = await executeRelease({ profile, environment: options['--environment'], context, adapter, store, request });
  await mkdir(dirname(resolve(options['--receipt'])), { recursive: true });
  await writeFile(resolve(options['--receipt']), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  console.log(JSON.stringify({ product: receipt.product, revision: receipt.revision, status: receipt.status }));
  if (receipt.status !== 'released') process.exitCode = 1;
}

if (process.argv[1] && await realpath(resolve(process.argv[1])).catch(() => null) === await realpath(fileURLToPath(import.meta.url))) {
  main(process.argv.slice(2)).catch(() => { console.error('Delivery refused; inspect the admission policy or retained release receipt.'); process.exitCode = 1; });
}
