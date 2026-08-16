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

function unavailable(provider, reason = 'provider model is unavailable') {
  return Buffer.from(JSON.stringify({ provider, available: false, configured: true, reason, summary: '', risk: 'low', findings: [] })).toString('base64');
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

function run({ openai = '', claude = '', ollama = '', truncated = false } = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'workslip-ai-review-'));
  const output = path.join(dir, 'output.txt');
  const result = spawnSync(process.execPath, [script], {
    cwd: dir,
    encoding: 'utf8',
    env: {
      ...process.env,
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
  const body = fs.readFileSync(path.join(dir, 'ai-review-body.md'), 'utf8');
  fs.rmSync(dir, { recursive: true, force: true });
  return { values, body };
}

assert.equal(
  run({
    openai: encoded('OpenAI', [finding('Tenant authorization can be bypassed')]),
    claude: encoded('Claude', [finding('Tenant authorization bypass on update')]),
  }).values.blocking,
  'true',
);
assert.equal(
  run({
    openai: encoded('OpenAI', [finding('Tenant authorization can be bypassed')]),
    ollama: encoded('Ollama', [finding('Tenant authorization bypass on update')]),
  }).values.blocking,
  'true',
);
assert.equal(
  run({
    claude: encoded('Claude', [finding('Tenant authorization can be bypassed')]),
    ollama: encoded('Ollama', [finding('Tenant authorization bypass on update')]),
  }).values.blocking,
  'true',
);
assert.equal(
  run({ openai: encoded('OpenAI', [finding('Tenant authorization can be bypassed')]) }).values.blocking,
  'false',
);
assert.equal(
  run({
    openai: encoded('OpenAI', [finding('Tenant authorization can be bypassed')]),
    ollama: encoded('Ollama', [finding('Tenant authorization bypass on update')]),
    truncated: true,
  }).values.blocking,
  'false',
);
assert.equal(run({}).values.providers, '0');

const ollamaOnly = run({
  openai: disabled('OpenAI'),
  claude: disabled('Claude'),
  ollama: encoded('Ollama', [finding('Tenant authorization bypass on update')]),
});
assert.equal(ollamaOnly.values.blocking, 'false');
assert.equal(ollamaOnly.values.providers, '1');
assert.equal(ollamaOnly.values.enabled, '1');

const claudeOnly = run({ openai: disabled('OpenAI'), claude: encoded('Claude', [finding('Tenant authorization bypass on update')]) });
assert.equal(claudeOnly.values.blocking, 'false');
assert.equal(claudeOnly.values.providers, '1');
assert.equal(claudeOnly.values.enabled, '1');

const threeProviders = run({
  openai: encoded('OpenAI', []),
  claude: encoded('Claude', []),
  ollama: encoded('Ollama', []),
});
assert.equal(threeProviders.values.providers, '3');
assert.equal(threeProviders.values.enabled, '3');

const degraded = run({
  openai: unavailable('OpenAI', 'configured model unavailable'),
  claude: encoded('Claude', []),
  ollama: disabled('Ollama'),
});
assert.equal(degraded.values.providers, '1');
assert.equal(degraded.values.enabled, '2');
assert.match(degraded.body, /configured model unavailable/);

const nothingConfigured = run({
  openai: disabled('OpenAI'),
  claude: disabled('Claude'),
  ollama: disabled('Ollama'),
});
assert.equal(nothingConfigured.values.blocking, 'false');
assert.equal(nothingConfigured.values.providers, '0');
assert.equal(nothingConfigured.values.enabled, '0');

assert.doesNotMatch(threeProviders.body, /GitHub Models/);

console.log('AI review supported-provider consensus tests passed');
