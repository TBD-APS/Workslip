import fs from 'node:fs';

const severityRank = { critical: 4, high: 3, medium: 2, low: 1 };
const marker = '<!-- workslip-ai-review -->';

function decode(value, provider) {
  if (!value) return { provider, available: false, reason: 'job produced no result', findings: [], summary: '', risk: 'low' };
  try {
    return JSON.parse(Buffer.from(value, 'base64').toString('utf8'));
  } catch {
    return { provider, available: false, reason: 'job result was unreadable', findings: [], summary: '', risk: 'low' };
  }
}

function tokens(text) {
  return new Set(String(text).toLowerCase().match(/[a-z0-9_]{3,}/g) || []);
}

function jaccard(a, b) {
  const left = tokens(a);
  const right = tokens(b);
  if (!left.size || !right.size) return 0;
  let common = 0;
  for (const token of left) if (right.has(token)) common += 1;
  return common / (left.size + right.size - common);
}

function sameFinding(a, b) {
  const sameFile = a.file && b.file && a.file === b.file;
  const nearbyLine = a.line && b.line ? Math.abs(a.line - b.line) <= 12 : true;
  const categoryMatch = a.category && b.category && a.category.toLowerCase() === b.category.toLowerCase();
  const titleMatch = jaccard(a.title, b.title) >= 0.45;
  if (sameFile) return nearbyLine && (categoryMatch || titleMatch);
  return categoryMatch && jaccard(a.title, b.title) >= 0.6;
}

const openai = decode(process.env.OPENAI_REVIEW_B64, 'OpenAI');
const claude = decode(process.env.CLAUDE_REVIEW_B64, 'Claude');
const reviews = [openai, claude];
const available = reviews.filter((review) => review.available);
const contextTruncated = process.env.CONTEXT_TRUNCATED === 'true';

const consensusPairs = [];
if (openai.available && claude.available && !contextTruncated) {
  for (const left of openai.findings) {
    if (severityRank[left.severity] < severityRank.high || left.confidence < 0.8) continue;
    for (const right of claude.findings) {
      if (severityRank[right.severity] < severityRank.high || right.confidence < 0.8) continue;
      if (sameFinding(left, right)) consensusPairs.push([left, right]);
    }
  }
}

const blocking = consensusPairs.length > 0;
const allFindings = reviews.flatMap((review) =>
  (review.findings || []).map((finding) => ({ ...finding, provider: review.provider })),
);
allFindings.sort((a, b) =>
  severityRank[b.severity] - severityRank[a.severity] || b.confidence - a.confidence,
);

const selected = [];
for (const finding of allFindings) {
  if (selected.some((existing) => sameFinding(existing, finding) && existing.provider !== finding.provider)) continue;
  selected.push(finding);
  if (selected.length >= 8) break;
}

const headSha = process.env.HEAD_SHA || '';
const prNumber = process.env.PR_NUMBER || '';
const status = available.length === 2 ? (blocking ? 'consensus blocker' : 'reviewed') : available.length === 1 ? 'degraded review' : 'not reviewed';

let body = `${marker}\n## Automated Workslip AI review\n\n`;
body += `**Status:** ${status} · **PR:** #${prNumber} · **SHA:** \`${headSha.slice(0, 12)}\`\n\n`;
body += `This comment is posted automatically through the configured Workslip review account. It is **not a human approval** and never merges code. OpenAI and Claude review independently; a blocking AI signal is emitted only when both independently identify a matching high/critical, high-confidence finding.\n\n`;

if (contextTruncated) {
  body += `> The diff exceeded the automated review context limit and was truncated. AI output is advisory only for this revision and cannot produce a consensus blocker.\n\n`;
}

for (const review of reviews) {
  if (review.available) {
    body += `### ${review.provider}\n${review.summary || 'No summary returned.'}\n\n`;
  } else {
    body += `### ${review.provider}\nUnavailable: ${review.reason || 'unknown reason'}.\n\n`;
  }
}

if (selected.length) {
  body += '### Findings\n';
  for (const finding of selected) {
    const location = finding.file ? ` — \`${finding.file}${finding.line ? `:${finding.line}` : ''}\`` : '';
    const consensus = consensusPairs.some(([a, b]) => sameFinding(a, finding) || sameFinding(b, finding));
    body += `\n- **${finding.severity.toUpperCase()}** (${finding.provider}, ${Math.round(finding.confidence * 100)}%${consensus ? ', consensus' : ''})${location}: ${finding.title}\n`;
    if (finding.evidence) body += `  - Evidence: ${finding.evidence}\n`;
    if (finding.recommendation) body += `  - Fix: ${finding.recommendation}\n`;
  }
  body += '\n';
} else if (available.length) {
  body += '### Findings\nNo actionable findings were returned for the supplied diff.\n\n';
}

if (!available.length) {
  body += 'Configure at least one model credential before relying on this workflow.\n\n';
}

body += 'Human review ownership and the existing CI/release gates remain unchanged.';

fs.writeFileSync('ai-review-body.md', body, 'utf8');
fs.writeFileSync('ai-review-result.json', JSON.stringify({ blocking, availableProviders: available.length, contextTruncated }, null, 2), 'utf8');

const output = process.env.GITHUB_OUTPUT;
if (output) {
  fs.appendFileSync(output, `blocking=${blocking}\nproviders=${available.length}\n`);
}
