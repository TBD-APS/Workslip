import fs from 'node:fs';

const provider = process.argv[2] || 'unknown';
const raw = process.env.RAW_REVIEW || '';
const actionOutcome = process.env.ACTION_OUTCOME || 'skipped';
const configured = process.env.PROVIDER_CONFIGURED === 'true';

function fallback(reason) {
  return {
    provider,
    available: false,
    reason,
    summary: '',
    risk: 'low',
    findings: [],
  };
}

let result;
if (!configured) {
  result = fallback('provider credential is not configured');
} else if (actionOutcome !== 'success') {
  result = fallback(`provider action ${actionOutcome}`);
} else {
  try {
    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed.summary !== 'string' || !Array.isArray(parsed.findings)) {
      throw new Error('unexpected structured output');
    }
    result = {
      provider,
      available: true,
      reason: '',
      summary: parsed.summary.slice(0, 1200),
      risk: ['low', 'medium', 'high', 'critical'].includes(parsed.risk) ? parsed.risk : 'medium',
      findings: parsed.findings.slice(0, 12).map((finding) => ({
        severity: ['critical', 'high', 'medium', 'low'].includes(finding.severity) ? finding.severity : 'medium',
        confidence: Math.max(0, Math.min(1, Number(finding.confidence) || 0)),
        category: String(finding.category || '').slice(0, 80),
        title: String(finding.title || '').slice(0, 180),
        file: String(finding.file || '').slice(0, 400),
        line: Number.isInteger(finding.line) && finding.line > 0 ? finding.line : null,
        evidence: String(finding.evidence || '').slice(0, 900),
        recommendation: String(finding.recommendation || '').slice(0, 900),
      })),
    };
  } catch (error) {
    result = fallback(`invalid structured output: ${error.message}`);
  }
}

fs.writeFileSync('provider-review.json', JSON.stringify(result), 'utf8');
