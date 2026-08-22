#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import process from 'node:process';
import { pathToFileURL } from 'node:url';

const PLAYWRIGHT_SCRIPT_PREFIX = 'src/FE/scripts/playwright-';
const EXCLUDED_FILES = new Set([
  'src/FE/scripts/playwright-stability-policy.mjs',
]);

function isScenarioFile(path) {
  return path.startsWith(PLAYWRIGHT_SCRIPT_PREFIX)
    && path.endsWith('.mjs')
    && !path.endsWith('.test.mjs')
    && !EXCLUDED_FILES.has(path);
}

function stableIdSelector(selector) {
  const value = selector.trim();
  return value.startsWith('#') || /^\[id(?:[\^$*|~]?=|\])/.test(value);
}

function literalMatches(source, patterns) {
  return patterns.flatMap((pattern) => [...source.matchAll(pattern)].map((match) => match[1]));
}

export function inspectAddedPlaywrightSelectors(diff) {
  const findings = [];
  let file = null;
  let newLine = 0;

  for (const rawLine of String(diff ?? '').split(/\r?\n/)) {
    if (rawLine.startsWith('+++ b/')) {
      file = rawLine.slice('+++ b/'.length);
      continue;
    }

    const hunk = rawLine.match(/^@@\s+-\d+(?:,\d+)?\s+\+(\d+)(?:,\d+)?\s+@@/);
    if (hunk) {
      newLine = Number(hunk[1]);
      continue;
    }

    if (!file || rawLine.startsWith('--- ')) continue;
    if (rawLine.startsWith('-')) continue;

    const isAdded = rawLine.startsWith('+') && !rawLine.startsWith('+++');
    const sourceLine = isAdded ? rawLine.slice(1) : rawLine;

    if (isAdded && isScenarioFile(file)) {
      if (/\bgetBy(?:Role|Text|Label|Placeholder|TestId|AltText|Title)\s*\(/.test(sourceLine)) {
        findings.push({
          file,
          line: newLine,
          rule: 'stable-id-required',
          message: 'New Playwright UI selectors must use stable DOM IDs, not getBy* copy/accessibility/test-id selectors.',
        });
      }

      if (/(?:hasText\s*:|:has-text\s*\(|\btext=|\[placeholder\s*=|\[aria-label\s*=)/.test(sourceLine)) {
        findings.push({
          file,
          line: newLine,
          rule: 'visible-copy-selector',
          message: 'Do not use visible copy, placeholders or accessible labels as Playwright selector plumbing.',
        });
      }

      const locatorSelectors = literalMatches(sourceLine, [
        /\.locator\(\s*'([^']+)'/g,
        /\.locator\(\s*"([^"]+)"/g,
        /\.locator\(\s*`([^`]+)`/g,
      ]);
      for (const selector of locatorSelectors) {
        if (!stableIdSelector(selector)) {
          findings.push({
            file,
            line: newLine,
            rule: 'non-id-locator',
            message: `New Playwright locator '${selector}' must target a stable DOM id.`,
          });
        }
      }

      const directSelectors = literalMatches(sourceLine, [
        /\bpage\.(?:click|fill|check|uncheck|hover|focus|press|selectOption|setInputFiles|waitForSelector)\(\s*'([^']+)'/g,
        /\bpage\.(?:click|fill|check|uncheck|hover|focus|press|selectOption|setInputFiles|waitForSelector)\(\s*"([^"]+)"/g,
        /\bpage\.(?:click|fill|check|uncheck|hover|focus|press|selectOption|setInputFiles|waitForSelector)\(\s*`([^`]+)`/g,
      ]);
      for (const selector of directSelectors) {
        if (!stableIdSelector(selector)) {
          findings.push({
            file,
            line: newLine,
            rule: 'non-id-direct-selector',
            message: `New direct Playwright selector '${selector}' must target a stable DOM id.`,
          });
        }
      }
    }

    if (!rawLine.startsWith('-')) newLine += 1;
  }

  return findings;
}

function gitDiff(base, head) {
  return execFileSync(
    'git',
    ['diff', '--unified=0', `${base}...${head}`, '--', ':(glob)src/FE/scripts/playwright-*.mjs'],
    { encoding: 'utf8' },
  );
}

function parseArgs(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--base') options.base = argv[++index];
    else if (argv[index] === '--head') options.head = argv[++index];
    else throw new Error(`Unknown argument: ${argv[index]}`);
  }
  if (!options.base || !options.head) throw new Error('--base and --head are required.');
  return options;
}

function main() {
  const { base, head } = parseArgs(process.argv.slice(2));
  const findings = inspectAddedPlaywrightSelectors(gitDiff(base, head));

  if (findings.length === 0) {
    console.log('Playwright selector contract passed: no new non-ID UI selector plumbing.');
    return;
  }

  console.error('PLAYWRIGHT_SELECTOR_CONTRACT_BLOCKED');
  for (const finding of findings) {
    console.error(`- ${finding.file}:${finding.line} [${finding.rule}] ${finding.message}`);
  }
  process.exitCode = 44;
}

const isEntryPoint = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isEntryPoint) {
  try {
    main();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}