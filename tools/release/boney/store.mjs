import { mkdir, open, readFile, readdir, unlink } from 'node:fs/promises';
import { resolve, join } from 'node:path';

export class FileReleaseStore {
  constructor(directory) { this.directory = resolve(directory); }

  async withLock(identity, callback) {
    if (![identity.product, identity.environment].every(value => /^[a-z][a-z0-9-]{0,63}$/.test(value ?? ''))
        || !/^[a-f0-9]{40}$/.test(identity.revision ?? '')) throw Error('RELEASE_IDENTITY_INVALID');
    const scope = join(this.directory, identity.product, identity.environment);
    await mkdir(scope, { recursive: true });
    const lockPath = join(scope, 'active.lock');
    let lock;
    try { lock = await open(lockPath, 'wx', 0o600); }
    catch (error) { if (error.code === 'EEXIST') throw Error('RELEASE_LOCKED'); throw error; }
    try {
      await lock.writeFile(JSON.stringify({ ...identity, pid: process.pid, startedAt: new Date().toISOString() }));
      await lock.sync();
      const directory = join(scope, identity.revision);
      await mkdir(directory, { recursive: true });
      const files = (await readdir(directory)).sort();
      if (files.some((file, index) => file !== `${String(index + 1).padStart(6, '0')}.json`)) throw Error('RELEASE_RECEIPTS_INVALID');
      let sequence = files.length, current = null;
      if (sequence) {
        try { current = JSON.parse(await readFile(join(directory, files.at(-1)), 'utf8')); }
        catch { throw Error('RELEASE_RECONCILIATION_REQUIRED'); }
        if (current.schemaVersion !== 1 || current.sequence !== sequence || current.product !== identity.product
            || current.environment !== identity.environment || current.revision !== identity.revision) throw Error('RELEASE_RECEIPTS_INVALID');
      }
      return await callback({
        load: () => current && structuredClone(current),
        save: async state => {
          const receipt = { ...state, ...identity, sequence: sequence + 1, recordedAt: new Date().toISOString() };
          const file = await open(join(directory, `${String(receipt.sequence).padStart(6, '0')}.json`), 'wx', 0o600);
          try { await file.writeFile(JSON.stringify(receipt, null, 2) + '\n'); await file.sync(); }
          finally { await file.close(); }
          sequence = receipt.sequence; current = receipt;
          return structuredClone(receipt);
        },
      });
    } finally {
      await lock.close();
      await unlink(lockPath);
    }
  }
}
