import fs from 'node:fs';

const apiKey = process.env.OPENAI_API_KEY || '';
const preferred = process.env.OPENAI_REVIEW_MODEL || '';
const fallbacks = (process.env.OPENAI_REVIEW_FALLBACKS || 'gpt-5.6,gpt-5.6-terra,gpt-5.6-luna,gpt-5.6-sol')
  .split(',')
  .map((value) => value.trim())
  .filter(Boolean);
const output = process.env.GITHUB_OUTPUT;

function write(values) {
  if (!output) return;
  const lines = Object.entries(values).map(([key, value]) => `${key}=${String(value).replaceAll('\n', ' ')}`);
  fs.appendFileSync(output, `${lines.join('\n')}\n`, 'utf8');
}

if (!apiKey) {
  write({ configured: false, available: false, model: '', reason: 'provider credential is not configured' });
  process.exit(0);
}

const candidates = [...new Set([preferred, ...fallbacks].filter(Boolean))];

try {
  const response = await fetch('https://api.openai.com/v1/models', {
    headers: { Authorization: `Bearer ${apiKey}` },
  });

  if (!response.ok) {
    write({ configured: true, available: false, model: '', reason: `model catalog request failed with HTTP ${response.status}` });
    process.exit(0);
  }

  const payload = await response.json();
  const available = new Set(Array.isArray(payload.data) ? payload.data.map((item) => item?.id).filter(Boolean) : []);
  const model = candidates.find((candidate) => available.has(candidate));

  if (!model) {
    write({ configured: true, available: false, model: '', reason: 'none of the configured review model candidates are available to this OpenAI project' });
    process.exit(0);
  }

  write({ configured: true, available: true, model, reason: '' });
  console.log(`Resolved OpenAI review model: ${model}`);
} catch (error) {
  write({ configured: true, available: false, model: '', reason: `model catalog request failed: ${error?.name || 'network error'}` });
}
