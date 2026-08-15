import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

const sourceRoot = path.resolve('src');
const forbiddenPatterns = [
  {
    label: 'global HTMLInputElement.focus monkeypatch',
    pattern: /HTMLInputElement\.prototype\.focus\s*=/,
  },
];

async function walk(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await walk(absolutePath));
      continue;
    }

    if (/\.(?:ts|tsx|js|jsx|mjs|cjs)$/.test(entry.name)) {
      files.push(absolutePath);
    }
  }

  return files;
}

const violations = [];
for (const file of await walk(sourceRoot)) {
  const source = await readFile(file, 'utf8');
  for (const forbidden of forbiddenPatterns) {
    if (forbidden.pattern.test(source)) {
      violations.push(`${path.relative(process.cwd(), file)}: ${forbidden.label}`);
    }
  }
}

if (violations.length > 0) {
  console.error('Prototype safety check failed:');
  for (const violation of violations) {
    console.error(`- ${violation}`);
  }
  process.exit(1);
}

console.log('Prototype safety check passed.');
