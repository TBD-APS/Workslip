import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const FRONTEND_ROOT = fileURLToPath(new URL('..', import.meta.url));
const SOURCE_ROOT = path.join(FRONTEND_ROOT, 'src');
const PRESENTATION_ROOT = path.join(SOURCE_ROOT, 'lib', 'presentation');
const SOURCE_EXTENSIONS = new Set(['.js', '.jsx', '.ts', '.tsx']);

// Date/time presentation is centrally owned. Number/text/currency migration is
// intentionally a separate follow-up so generic Number#toLocaleString calls are
// not part of this gate yet.
const forbidden = [
  { label: 'Intl.DateTimeFormat', pattern: /\bIntl\.DateTimeFormat\b/g },
  { label: 'toLocaleDateString', pattern: /\.toLocaleDateString\s*\(/g },
  { label: 'toLocaleTimeString', pattern: /\.toLocaleTimeString\s*\(/g },
  { label: 'new Date(...).toLocaleString', pattern: /new\s+Date\s*\([^)]*\)\.toLocaleString\s*\(/g },
];

// Transitional baseline. These are pre-existing date/time call sites that are
// intentionally migrated in the next presentation/localization PR. The gate
// rejects any increase and disappears as the baseline is paid down.
const legacyAllowance = new Map([
  ['src/features/jobs/components/JobConversationDrawer.tsx', 2],
  ['src/features/settings/routes/Settings.tsx', 1],
  ['src/features/superadmin/diagnostics/ErrorDiagnosticsDashboard.tsx', 1],
  ['src/features/superadmin/routes/CacheDiagnostics.tsx', 1],
  ['src/features/worksheets/routes/MyWorksheets.tsx', 3],
]);

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

const findings = [];
const files = await collectSourceFiles(SOURCE_ROOT);

for (const file of files) {
  if (file.startsWith(`${PRESENTATION_ROOT}${path.sep}`)) continue;

  const source = await readFile(file, 'utf8');
  for (const { label, pattern } of forbidden) {
    pattern.lastIndex = 0;
    for (const match of source.matchAll(pattern)) {
      findings.push({
        file: path.relative(FRONTEND_ROOT, file),
        line: lineNumberAt(source, match.index ?? 0),
        label,
      });
    }
  }
}

const findingsByFile = new Map();
for (const finding of findings) {
  const current = findingsByFile.get(finding.file) ?? [];
  current.push(finding);
  findingsByFile.set(finding.file, current);
}

const violations = [];
for (const [file, fileFindings] of findingsByFile) {
  const allowance = legacyAllowance.get(file) ?? 0;
  if (fileFindings.length > allowance) {
    violations.push(...fileFindings.slice(allowance));
  }
}

if (violations.length > 0) {
  console.error('New date/time presentation boundary violations found:');
  for (const violation of violations) {
    console.error(`- ${violation.file}:${violation.line} uses ${violation.label}`);
  }
  console.error('Move user-visible date/time formatting into src/lib/presentation/.');
  process.exit(1);
}

const legacyCount = findings.length;
console.log(
  `Date/time presentation boundary passed (${files.length} source files scanned; ${legacyCount} legacy call sites tracked for follow-up).`,
);
