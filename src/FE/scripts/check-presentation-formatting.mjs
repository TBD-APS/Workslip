import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const FRONTEND_ROOT = fileURLToPath(new URL('..', import.meta.url));
const SOURCE_ROOT = path.join(FRONTEND_ROOT, 'src');
const PRESENTATION_ROOT = path.join(SOURCE_ROOT, 'lib', 'presentation');
const SOURCE_EXTENSIONS = new Set(['.js', '.jsx', '.ts', '.tsx']);

const forbidden = [
  { label: 'Intl.DateTimeFormat', pattern: /\bIntl\.DateTimeFormat\b/g },
  { label: 'toLocaleDateString', pattern: /\.toLocaleDateString\s*\(/g },
  { label: 'toLocaleTimeString', pattern: /\.toLocaleTimeString\s*\(/g },
  { label: 'toLocaleString', pattern: /\.toLocaleString\s*\(/g },
];

async function collectSourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collectSourceFiles(absolutePath));
      continue;
    }
    if (SOURCE_EXTENSIONS.has(path.extname(entry.name))) files.push(absolutePath);
  }

  return files;
}

function lineNumberAt(source, offset) {
  return source.slice(0, offset).split('\n').length;
}

const violations = [];
const files = await collectSourceFiles(SOURCE_ROOT);

for (const file of files) {
  if (file.startsWith(`${PRESENTATION_ROOT}${path.sep}`)) continue;

  const source = await readFile(file, 'utf8');
  for (const { label, pattern } of forbidden) {
    pattern.lastIndex = 0;
    for (const match of source.matchAll(pattern)) {
      violations.push({
        file: path.relative(FRONTEND_ROOT, file),
        line: lineNumberAt(source, match.index ?? 0),
        label,
      });
    }
  }
}

if (violations.length > 0) {
  console.error('Presentation formatting boundary violations found:');
  for (const violation of violations) {
    console.error(`- ${violation.file}:${violation.line} uses ${violation.label}`);
  }
  console.error('Move locale-sensitive presentation formatting into src/lib/presentation/.');
  process.exit(1);
}

console.log(`Presentation formatting boundary passed (${files.length} source files scanned).`);
