import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const script = new URL('./aggregate.mjs', import.meta.url).pathname;

function encoded(provider, findings) {
  return Buffer.from(JSON.stringify({ provider, available: true, reason: '', summary: `${provider} summary`, risk: 'high', findings })).toString('base64');
}

function finding(title, confidence = 0.91) {
  return {
    severity: 'high',
    confidence,
    category: 'authorization',
    title,
    file: 'src/BE/Example.cs',
    line: 42,
    evidence: 'Server-side tenant check is bypassed.',
    recommendation: 'Restore the tenant ownership guard.',
  };
}

function run({ githubModels = '', openai = '', claude = '', truncated = false } = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'workslip-ai-review-'));
  const output = path.join(dir, 'output.txt');
  const result = spawnSync(process.execPath, [script], {
    cwd: dir,
    encoding: 'utf8',
    env: {
      ...process.env,
      GITHUB_MODELS_REVIEW_B64: githubModels,
      OPENAI_REVIEW_B64: openai,
      CLAUDE_REVIEW_B64: claude,
      CONTEXT_TRUNCATED: String(truncated),
      PR_NUMBER: '123',
      HEAD_SHA: 'abcdef1234567890',
      GITHUB_OUTPUT: output,
    },
  });
  assert.equal(result.status, 0, result.stderr);
  const values = Object.fromEntries(fs.readFileSync(output, 'utf8').trim().split('\n').map((line) => line.split('=')));
  fs.rmSync(dir, { recursive: true, force: true });
  return values;
}

assert.equal(
  run({
    githubModels: encoded('GitHub Models', [finding('Tenant authorization can be bypassed')]),
    openai: encoded('OpenAI', [finding('Tenant authorization bypass on update')]),
  }).blocking,
  'true',
);
assert.equal(
  run({
    githubModels: encoded('GitHub Models', [finding('Tenant authorization can be bypassed')]),
    claude: encoded('Claude', [finding('Tenant authorization bypass on update')]),
  }).blocking,
  'true',
);
assert.equal(
  run({ githubModels: encoded('GitHub Models', [finding('Tenant authorization can be bypassed')]) }).blocking,
  'false',
);
assert.equal(
  run({
    githubModels: encoded('GitHub Models', [finding('Tenant authorization can be bypassed')]),
    openai: encoded('OpenAI', [finding('Tenant authorization bypass on update')]),
    truncated: true,
  }).blocking,
  'false',
);
assert.equal(run({}).providers, '0');

console.log('AI review multi-provider consensus tests passed');
