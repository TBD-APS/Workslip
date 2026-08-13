#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import process from 'node:process';
import { pathToFileURL } from 'node:url';

const PRODUCT_PREFIXES = [
  'src/FE/src/features/',
  'src/FE/src/components/',
  'src/BE/WorkslipApi/Endpoints/',
  'src/BE/WorkslipApi/Workslip.Application/',
  'src/BE/WorkslipApi/Workslip.Domain/',
  'src/BE/WorkslipApi/Workslip.Infrastructure/',
];

export function isProductPath(path) {
  return PRODUCT_PREFIXES.some((prefix) => path.startsWith(prefix));
}

export function parseNumstat(text) {
  return text
    .split(/\r?\n/)
    .filter(Boolean)
    .map((line) => {
      const [addedRaw, deletedRaw, ...pathParts] = line.split('\t');
      const path = pathParts.join('\t');
      return {
        path,
        added: addedRaw === '-' ? 0 : Number.parseInt(addedRaw, 10) || 0,
        deleted: deletedRaw === '-' ? 0 : Number.parseInt(deletedRaw, 10) || 0,
      };
    });
}

export function parseNameStatus(text) {
  return text
    .split(/\r?\n/)
    .filter(Boolean)
    .map((line) => {
      const [status, ...pathParts] = line.split('\t');
      return { status, path: pathParts.at(-1) ?? '' };
    });
}

export function classifyChangeRisk({ numstat, nameStatus }) {
  const productStats = numstat.filter((entry) => isProductPath(entry.path));
  const productDeletedFiles = nameStatus
    .filter((entry) => entry.status.startsWith('D') && isProductPath(entry.path))
    .map((entry) => entry.path);

  const productAdded = productStats.reduce((sum, entry) => sum + entry.added, 0);
  const productDeleted = productStats.reduce((sum, entry) => sum + entry.deleted, 0);
  const productChanged = productAdded + productDeleted;
  const deletionRatio = productChanged === 0 ? 0 : productDeleted / productChanged;

  const featureAreas = new Set(
    productStats
      .filter((entry) => entry.path.startsWith('src/FE/src/features/'))
      .map((entry) => entry.path.split('/').slice(0, 5).join('/')),
  );

  const reasons = [];
  if (productDeletedFiles.length >= 3) {
    reasons.push(`${productDeletedFiles.length} product-code files are deleted`);
  }
  if (productDeleted >= 400 && deletionRatio >= 0.60) {
    reasons.push(`${productDeleted} product-code lines are deleted (${Math.round(deletionRatio * 100)}% deletion ratio)`);
  }
  if (featureAreas.size >= 2 && productDeleted >= 250 && deletionRatio >= 0.50) {
    reasons.push(`deletions span ${featureAreas.size} frontend feature areas`);
  }

  return {
    highRisk: reasons.length > 0,
    reasons,
    productAdded,
    productDeleted,
    deletionRatio,
    productDeletedFiles,
    featureAreas: [...featureAreas].sort(),
  };
}

function gitDiff(args) {
  return execFileSync('git', ['diff', ...args], { encoding: 'utf8' });
}

function parseArgs(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === '--base') options.base = argv[++index];
    else if (arg === '--head') options.head = argv[++index];
    else throw new Error(`Unknown argument: ${arg}`);
  }
  if (!options.base || !options.head) throw new Error('--base and --head are required.');
  return options;
}

function writeOutput(result) {
  const summary = [
    `Product additions: ${result.productAdded}`,
    `Product deletions: ${result.productDeleted}`,
    `Deletion ratio: ${Math.round(result.deletionRatio * 100)}%`,
    `Deleted product files: ${result.productDeletedFiles.length}`,
  ];

  console.log(summary.join('\n'));
  if (result.productDeletedFiles.length > 0) {
    console.log('\nDeleted product files:');
    for (const file of result.productDeletedFiles) console.log(`- ${file}`);
  }

  if (result.highRisk) {
    console.error('\nHIGH_RISK_FEATURE_REMOVAL');
    for (const reason of result.reasons) console.error(`- ${reason}`);
  } else {
    console.log('\nChange does not cross the high-risk feature-removal threshold.');
  }

  if (process.env.GITHUB_OUTPUT) {
    const fs = requireFs();
    fs.appendFileSync(process.env.GITHUB_OUTPUT, `high_risk=${result.highRisk}\n`);
  }
}

function requireFs() {
  // Keep the classifier import side-effect free for node:test.
  return globalThis.__workslipFs ??= awaitImportFs();
}

function awaitImportFs() {
  // eslint-disable-next-line global-require
  return require('node:fs');
}

async function main() {
  const { base, head } = parseArgs(process.argv.slice(2));
  const numstat = parseNumstat(gitDiff(['--numstat', `${base}...${head}`]));
  const nameStatus = parseNameStatus(gitDiff(['--name-status', '--find-renames', `${base}...${head}`]));
  const result = classifyChangeRisk({ numstat, nameStatus });

  const { appendFileSync } = await import('node:fs');
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, `high_risk=${result.highRisk}\n`);
  }
  writeOutput({ ...result, _skipOutputWrite: true });
  if (result.highRisk) process.exitCode = 42;
}

const isEntryPoint = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isEntryPoint) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
