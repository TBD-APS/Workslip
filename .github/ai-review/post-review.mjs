const token = process.env.REVIEW_TOKEN;
const repo = process.env.GITHUB_REPOSITORY;
const prNumber = process.env.PR_NUMBER;
const body = await import('node:fs').then(({ readFileSync }) => readFileSync('ai-review-body.md', 'utf8'));
const marker = '<!-- workslip-ai-review -->';
const publisherLogin = String(process.env.REVIEW_PUBLISHER_LOGIN || 'github-actions[bot]').toLowerCase();

if (!token) throw new Error('REVIEW_TOKEN is missing.');
if (!repo || !prNumber) throw new Error('Repository or PR number missing.');
if (!publisherLogin) throw new Error('Review publisher login is missing.');

const headers = {
  Accept: 'application/vnd.github+json',
  Authorization: `Bearer ${token}`,
  'X-GitHub-Api-Version': '2022-11-28',
  'User-Agent': 'workslip-ai-review',
};

async function api(path, options = {}) {
  const response = await fetch(`https://api.github.com${path}`, { ...options, headers: { ...headers, ...(options.headers || {}) } });
  const text = await response.text();
  if (!response.ok) throw new Error(`GitHub API ${response.status}: ${text}`);
  return text ? JSON.parse(text) : null;
}

// GITHUB_TOKEN is an installation token, not a user token. Calling GET /user with
// it can return 403 even when issue-comment writes are correctly scoped. Keep the
// ownership rule deterministic instead: this workflow only updates marker comments
// created by the GitHub Actions bot identity. A human/PAT-authored marker comment is
// never modified.
const comments = await api(`/repos/${repo}/issues/${prNumber}/comments?per_page=100`);
const existing = comments.find((comment) =>
  typeof comment.body === 'string'
  && comment.body.includes(marker)
  && String(comment.user?.login || '').toLowerCase() === publisherLogin,
);

if (existing) {
  await api(`/repos/${repo}/issues/comments/${existing.id}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ body }),
  });
  console.log(`Updated bot-owned AI review comment ${existing.id}.`);
} else {
  const created = await api(`/repos/${repo}/issues/${prNumber}/comments`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ body }),
  });
  console.log(`Created AI review comment ${created.id} for ${publisherLogin}.`);
}
