import { readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

export function collectErrorFingerprints(results) {
  const counts = new Map();
  const details = new Map();

  for (const result of results) {
    const relativePath = normalizeFilePath(result.filePath ?? 'unknown');
    const sourceLines = typeof result.source === 'string' ? result.source.split(/\r?\n/) : [];

    for (const message of result.messages ?? []) {
      if (message.severity !== 2) continue;

      const fingerprint = JSON.stringify([
        relativePath,
        message.ruleId ?? 'fatal',
        message.message ?? '',
        sourceExcerpt(sourceLines, message),
      ]);

      counts.set(fingerprint, (counts.get(fingerprint) ?? 0) + 1);
      details.set(fingerprint, {
        filePath: relativePath,
        ruleId: message.ruleId ?? 'fatal',
        message: message.message ?? '',
        line: message.line ?? null,
      });
    }
  }

  return { counts, details };
}

export function findNewErrors(baselineResults, currentResults) {
  const baseline = collectErrorFingerprints(baselineResults);
  const current = collectErrorFingerprints(currentResults);
  const additions = [];

  for (const [fingerprint, currentCount] of current.counts) {
    const baselineCount = baseline.counts.get(fingerprint) ?? 0;
    const addedCount = currentCount - baselineCount;
    if (addedCount <= 0) continue;

    additions.push({
      ...current.details.get(fingerprint),
      count: addedCount,
    });
  }

  return additions.sort((left, right) =>
    left.filePath.localeCompare(right.filePath)
    || (left.line ?? 0) - (right.line ?? 0)
    || left.ruleId.localeCompare(right.ruleId));
}

function normalizeFilePath(filePath) {
  const normalized = path.resolve(filePath).replaceAll('\\', '/');
  const marker = '/src/FE/';
  const markerIndex = normalized.lastIndexOf(marker);
  return markerIndex >= 0 ? normalized.slice(markerIndex + marker.length) : normalized;
}

function sourceExcerpt(sourceLines, message) {
  if (!message.line || sourceLines.length === 0) return '';
  const start = Math.max(0, message.line - 1);
  const end = Math.min(sourceLines.length, message.endLine ?? message.line);
  return sourceLines
    .slice(start, end)
    .map((line) => line.trim())
    .join('\n');
}

async function readResults(filePath) {
  const value = JSON.parse(await readFile(filePath, 'utf8'));
  if (!Array.isArray(value)) throw new Error(`${filePath} does not contain ESLint JSON results.`);
  return value;
}

async function main() {
  const [baselinePath, currentPath] = process.argv.slice(2);
  if (!baselinePath || !currentPath) {
    throw new Error('Usage: node scripts/compare-eslint-results.mjs <baseline.json> <current.json>');
  }

  const [baselineResults, currentResults] = await Promise.all([
    readResults(baselinePath),
    readResults(currentPath),
  ]);
  const additions = findNewErrors(baselineResults, currentResults);
  const baselineErrorCount = baselineResults.reduce((sum, result) => sum + (result.errorCount ?? 0), 0);
  const currentErrorCount = currentResults.reduce((sum, result) => sum + (result.errorCount ?? 0), 0);

  process.stdout.write(`ESLint error debt: base=${baselineErrorCount}, current=${currentErrorCount}.\n`);

  if (additions.length === 0) {
    process.stdout.write('No new ESLint errors introduced.\n');
    return;
  }

  process.stderr.write(`New ESLint errors introduced (${additions.reduce((sum, item) => sum + item.count, 0)}):\n`);
  for (const item of additions) {
    const location = item.line ? `:${item.line}` : '';
    const suffix = item.count > 1 ? ` (${item.count} occurrences)` : '';
    process.stderr.write(`- ${item.filePath}${location} [${item.ruleId}] ${item.message}${suffix}\n`);
  }
  process.exitCode = 1;
}

const isDirectRun = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(new URL(import.meta.url).pathname);
if (isDirectRun) await main();
