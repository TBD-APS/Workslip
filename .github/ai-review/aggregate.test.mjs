import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const script = new URL('./aggregate.mjs', import.meta.url).pathname;

function encoded(provider, findings) {
  return Buffer.from(JSON.stringify({ provider, available: true, configured: true, reason: '', summary: `${provider} summary`, risk: 'high', findings })).toString('base64');
}

function disabled(provider) {
  return Buffer.from(JSON.stringify({ provider, available: false, configured: false, reason: 'provider credential is not configured', summary: '', risk: 'low', findings: [] })).toString('base64');
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

function run({ githubModels = '', openai = '', claude = '', ollama = '', truncated = false } = {}) {
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
      OLLAMA_REVIEW_B64: ollama,
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
  run({
    githubModels: encoded('GitHub Models', [finding('Tenant authorization can be bypassed')]),
    ollama: encoded('Ollama', [finding('Tenant authorization bypass on update')]),
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
    ollama: encoded('Ollama', [finding('Tenant authorization bypass on update')]),
    truncated: true,
  }).blocking,
  'false',
);
assert.equal(run({}).providers, '0');

const ollamaOnly = run({
  githubModels: disabled('GitHub Models'),
  openai: disabled('OpenAI'),
  claude: disabled('Claude'),
  ollama: encoded('Ollama', [finding('Tenant authorization bypass on update')]),
});
assert.equal(ollamaOnly.blocking, 'false');
assert.equal(ollamaOnly.providers, '1');
assert.equal(ollamaOnly.enabled, '1');

const claudeOnly = run({ openai: disabled('OpenAI'), claude: encoded('Claude', [finding('Tenant authorization bypass on update')]) });
assert.equal(claudeOnly.blocking, 'false');
assert.equal(claudeOnly.providers, '1');
assert.equal(claudeOnly.enabled, '1');

const threeProviders = run({
  githubModels: encoded('GitHub Models', []),
  claude: encoded('Claude', []),
  ollama: encoded('Ollama', []),
});
assert.equal(threeProviders.providers, '3');
assert.equal(threeProviders.enabled, '3');

const nothingConfigured = run({
  githubModels: disabled('GitHub Models'),
  openai: disabled('OpenAI'),
  claude: disabled('Claude'),
  ollama: disabled('Ollama'),
});
assert.equal(nothingConfigured.blocking, 'false');
assert.equal(nothingConfigured.providers, '0');
assert.equal(nothingConfigured.enabled, '0');

console.log('AI review multi-provider consensus tests passed');
