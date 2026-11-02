import { readFile, readdir, lstat } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash } from 'node:crypto';

export const RUNTIME_FILES = Object.freeze(['cli.mjs', 'github.mjs', 'pipeline.mjs', 'profile.mjs', 'risk.mjs', 'scaffold.mjs', 'snapshot.mjs', 'store.mjs']);

export async function verifySnapshot(directory) {
  const fail = () => { throw Error('DELIVERY_SNAPSHOT_INVALID'); };
  const lock = JSON.parse(await readFile(join(directory, 'boney.lock.json'), 'utf8'));
  if (lock.schemaVersion !== 1 || lock.sourceRepository !== 'TBD-APS/boney' || !/^[a-f0-9]{40}$/.test(lock.sourceRevision ?? '')
      || !lock.files || JSON.stringify(Object.keys(lock.files).sort()) !== JSON.stringify([...RUNTIME_FILES].sort())) fail();
  const names = (await readdir(directory)).sort();
  if (JSON.stringify(names) !== JSON.stringify([...RUNTIME_FILES, 'boney.lock.json'].sort())) fail();
  for (const name of RUNTIME_FILES) {
    if (!(await lstat(join(directory, name))).isFile() || !/^[a-f0-9]{64}$/.test(lock.files[name] ?? '')) fail();
    if (createHash('sha256').update(await readFile(join(directory, name))).digest('hex') !== lock.files[name]) fail();
  }
  return lock;
}
