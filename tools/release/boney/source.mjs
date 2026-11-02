import { lstat, realpath } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { isAbsolute, relative, resolve, sep } from 'node:path';

// Policy and executable entrypoints must come from the admitted Git tree.
// Git status alone can miss ignored paths, symlinks and assume-unchanged files.
export async function requireTrackedSources({ revision, paths, cwd = process.cwd() }) {
  const git = args => execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  if (!/^[a-f0-9]{40}$/.test(revision ?? '') || git(['rev-parse', 'HEAD']).trim() !== revision
      || git(['status', '--porcelain', '--untracked-files=normal'])) throw Error('DELIVERY_REQUIRES_CLEAN_EXACT_SOURCE');
  if (!Array.isArray(paths) || !paths.length) throw Error('DELIVERY_SOURCE_INVALID');
  const root = await realpath(git(['rev-parse', '--show-toplevel']).trim());
  const base = await realpath(cwd), sources = [];
  for (const path of paths) {
    try {
      if (typeof path !== 'string' || !path || /[\r\n\0]/.test(path)) throw Error();
      const absolute = resolve(base, path), canonical = await realpath(absolute);
      const name = relative(root, canonical);
      if (!name || name === '..' || name.startsWith(`..${sep}`) || isAbsolute(name) || !(await lstat(absolute)).isFile()) throw Error();
      // Reject links within the source path, while allowing OS aliases above the repository.
      const logicalName = relative(root, absolute);
      if (!isAbsolute(logicalName) && logicalName !== '..' && !logicalName.startsWith(`..${sep}`)) {
        let current = root;
        for (const segment of logicalName.split(sep)) {
          current = resolve(current, segment);
          if ((await lstat(current)).isSymbolicLink()) throw Error();
        }
      }
      const entry = git(['ls-tree', '-z', revision, '--', `:(literal)${name}`]);
      const match = /^(100644|100755) blob ([a-f0-9]{40})\t([^\0]+)\0$/.exec(entry);
      if (!match || match[3] !== name || git(['hash-object', '--no-filters', '--', canonical]).trim() !== match[2]) throw Error();
      sources.push(canonical);
    } catch { throw Error('DELIVERY_SOURCE_INVALID'); }
  }
  return sources;
}
