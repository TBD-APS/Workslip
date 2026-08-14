import fs from 'node:fs';

const token = process.env.REVIEW_TOKEN;
const repo = process.env.GITHUB_REPOSITORY;
const prNumber = process.env.PR_NUMBER;
const apiUrl = (process.env.GITHUB_API_URL || 'https://api.github.com').replace(/\/$/, '');
const expectedLogin = process.env.REVIEW_ACCOUNT;
const body = fs.readFileSync('ai-review-body.md', 'utf8');
const marker = '<!-- workslip-ai-review -->';

if (!token) throw new Error('WORKSLIP_REVIEW_PAT is not configured');

async function api(path, options = {}) {
  const response = await fetch(`${apiUrl}${path}`, {
    ...options,
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${token}`,
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'workslip-ai-review',
      'Content-Type': 'application/json',
      ...(options.headers || {}),
    },
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`GitHub API ${response.status}: ${text.slice(0, 600)}`);
  }
  return response.status === 204 ? null : response.json();
}

async function findExistingComment(login) {
  for (let page = 1; page <= 20; page += 1) {
    const comments = await api(`/repos/${repo}/issues/${prNumber}/comments?per_page=100&page=${page}`);
    const existing = comments.find(
      (comment) => comment.user?.login?.toLowerCase() === login.toLowerCase() && comment.body?.includes(marker),
    );
    if (existing) return existing;
    if (comments.length < 100) return null;
  }
  throw new Error('PR has more than 2000 conversation comments; refusing to create a duplicate AI review comment');
}

const user = await api('/user');
if (expectedLogin && user.login.toLowerCase() !== expectedLogin.toLowerCase()) {
  throw new Error(`WORKSLIP_REVIEW_PAT belongs to ${user.login}, expected ${expectedLogin}`);
}

const existing = await findExistingComment(user.login);
if (existing) {
  await api(`/repos/${repo}/issues/comments/${existing.id}`, { method: 'PATCH', body: JSON.stringify({ body }) });
  console.log(`Updated automated review comment ${existing.id} as ${user.login}.`);
} else {
  const created = await api(`/repos/${repo}/issues/${prNumber}/comments`, { method: 'POST', body: JSON.stringify({ body }) });
  console.log(`Created automated review comment ${created.id} as ${user.login}.`);
}
