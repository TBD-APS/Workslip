import { mkdir, writeFile } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { loadProfile } from './profile.mjs';
import { executeRelease, authorizeRelease } from './pipeline.mjs';
import { FileReleaseStore } from './store.mjs';
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
  if (command === 'validate-profile' && rest.length === 1) { const profile = await loadProfile(resolve(rest[0])); console.log(JSON.stringify({ product: profile.product, schemaVersion: profile.schemaVersion })); return; }
  if (!['run', 'admit'].includes(command) || rest.length % 2) throw Error('DELIVERY_USAGE');
  const options = {};
  for (let index = 0; index < rest.length; index += 2) {
    const key = rest[index];
    if (!['--profile', '--adapter', '--environment', '--revision', '--store', '--receipt'].includes(key) || Object.hasOwn(options, key) || !rest[index + 1]) throw Error('DELIVERY_USAGE');
    options[key] = rest[index + 1];
  }
  if (!options['--profile'] || !options['--revision']) throw Error('DELIVERY_USAGE');
  const profile = await loadProfile(resolve(options['--profile']));
  const context = { repository: process.env.GITHUB_REPOSITORY, ref: process.env.GITHUB_REF, event: process.env.GITHUB_EVENT_NAME, sha: options['--revision'] };
  const request = githubClient();
  if (command === 'admit') { console.log(JSON.stringify(await authorizeRelease(profile, context, request))); return; }
  if (!options['--adapter'] || !options['--environment'] || !options['--receipt']) throw Error('DELIVERY_USAGE');
  const head = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  const dirty = execFileSync('git', ['status', '--porcelain', '--untracked-files=normal'], { encoding: 'utf8' });
  if (head !== context.sha || dirty) throw Error('DELIVERY_REQUIRES_CLEAN_EXACT_SOURCE');
  // Admission precedes loading executable product code.
  await authorizeRelease(profile, context, request);
  const adapterPath = resolve(options['--adapter']);
  execFileSync('git', ['ls-files', '--error-unmatch', '--', adapterPath], { stdio: 'pipe' });
  const adapter = await import(pathToFileURL(adapterPath).href);
  const store = options['--store'] ? new FileReleaseStore(options['--store'])
    : typeof adapter.createStore === 'function' ? await adapter.createStore({ profile, environment: options['--environment'], context }) : null;
  if (!store || typeof store.withLock !== 'function') throw Error('DURABLE_RELEASE_STORE_REQUIRED');
  const receipt = await executeRelease({ profile, environment: options['--environment'], context, adapter, store, request });
  await mkdir(dirname(resolve(options['--receipt'])), { recursive: true });
  await writeFile(resolve(options['--receipt']), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  console.log(JSON.stringify({ product: receipt.product, revision: receipt.revision, status: receipt.status }));
  if (receipt.status !== 'released') process.exitCode = 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main(process.argv.slice(2)).catch(() => { console.error('Delivery refused; inspect the admission policy or retained release receipt.'); process.exitCode = 1; });
}
