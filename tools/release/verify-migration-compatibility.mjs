import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { execFileSync } from 'node:child_process';

const MIGRATION_ROOT = 'src/BE/infrastructure/database/migrations/';
const DESTRUCTIVE_PATTERNS = [
  /\bDROP\s+TABLE\b/i,
  /\bDROP\s+COLUMN\b/i,
  /\bTRUNCATE\s+TABLE\b/i,
  /\bALTER\s+TABLE\b[\s\S]*?\bALTER\s+COLUMN\b/i,
  /\bsp_rename\b/i,
  /\bDROP\s+CONSTRAINT\b/i,
];
const EXPLICIT_CONTRACT_MARKER = /WORKSLIP-CONTRACT-MIGRATION:\s*approved/i;

export function inspectMigration(name, sql) {
  const hits = DESTRUCTIVE_PATTERNS.filter((pattern) => pattern.test(sql)).map((pattern) => pattern.source);
  return {
    name,
    destructive: hits.length > 0,
    approvedContract: EXPLICIT_CONTRACT_MARKER.test(sql),
    hits,
  };
}

function changedMigrationFiles(base, head) {
  const output = execFileSync('git', ['diff', '--name-only', `${base}..${head}`, '--', MIGRATION_ROOT], {
    encoding: 'utf8',
  });
  return output.split(/\r?\n/).map((value) => value.trim()).filter(Boolean);
}

function main() {
  const [base, head = 'HEAD'] = process.argv.slice(2);
  if (!base) throw new Error('Usage: node verify-migration-compatibility.mjs <base-sha> [head-sha]');

  const files = changedMigrationFiles(base, head);
  if (files.length === 0) {
    console.log('[migration-policy] no migration changes detected.');
    return;
  }

  const violations = [];
  for (const file of files) {
    if (!file.endsWith('.sql')) continue;
    if (!fs.existsSync(file)) continue;
    const sql = fs.readFileSync(file, 'utf8');
    const result = inspectMigration(path.basename(file), sql);
    if (result.destructive && !result.approvedContract) violations.push(result);
  }

  if (violations.length > 0) {
    const details = violations.map((item) => `- ${item.name}: destructive schema operation detected`).join('\n');
    throw new Error(
      `Production migration policy blocks destructive/non-backward-compatible migrations in the same release as application rollout.\n${details}\nUse an expand migration first. A later dedicated contract migration requires the comment marker: -- WORKSLIP-CONTRACT-MIGRATION: approved`,
    );
  }

  console.log(`[migration-policy] ${files.length} changed migration file(s) satisfy expand/contract policy.`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(new URL(import.meta.url).pathname)) {
  try {
    main();
  } catch (error) {
    console.error(`[migration-policy] blocked: ${error.message}`);
    process.exitCode = 1;
  }
}
