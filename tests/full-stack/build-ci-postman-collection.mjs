import fs from 'node:fs';
import path from 'node:path';

const [sourceArg, outputArg] = process.argv.slice(2);
if (!sourceArg || !outputArg) {
  console.error('Usage: node build-ci-postman-collection.mjs <source> <output>');
  process.exit(64);
}

const source = path.resolve(sourceArg);
const output = path.resolve(outputArg);
const collection = JSON.parse(fs.readFileSync(source, 'utf8'));

const excluded = [
  { method: 'POST', pattern: /\/api\/organizations\/?(?:\?|$)/i, reason: 'created during isolated-stack bootstrap' },
  { method: 'POST', pattern: /\/api\/auth\/send-code(?:\?|$)/i, reason: 'requires external email delivery' },
  { method: 'POST', pattern: /\/api\/auth\/verify-code\//i, reason: 'requires an out-of-band email code' },
  { method: 'POST', pattern: /\/api\/auth\/entra-(?:login|enroll)(?:\?|$)/i, reason: 'requires Microsoft Entra' },
  { method: 'POST', pattern: /\/api\/auth\/invite(?:\?|$)/i, reason: 'requires external invitation delivery' },
  { method: 'POST', pattern: /\/api\/auth\/invite\/[^/]+\/open(?:\?|$)/i, reason: 'requires a real invitation token' },
  { method: 'POST', pattern: /\/api\/push-subscriptions\/?(?:\?|$)/i, reason: 'requires a browser push subscription' },
  { method: 'POST', pattern: /\/api\/admin\/cache\/clear(?:\?|$)/i, reason: 'may call external cache invalidation' },
];

function rawUrl(request) {
  const raw = request?.url?.raw;
  if (typeof raw === 'string') return raw;
  if (typeof request?.url === 'string') return request.url;
  return '';
}

const skipped = [];
let retainedRequests = 0;

function filterItems(items, parents = []) {
  const result = [];
  for (const item of items ?? []) {
    if (item.request) {
      const method = String(item.request.method ?? 'GET').toUpperCase();
      const url = rawUrl(item.request);
      const rule = excluded.find(entry => entry.method === method && entry.pattern.test(url));
      if (rule) {
        skipped.push({ name: [...parents, item.name].join(' / '), method, url, reason: rule.reason });
        continue;
      }
      retainedRequests += 1;
      result.push(item);
      continue;
    }

    if (Array.isArray(item.item)) {
      const children = filterItems(item.item, [...parents, item.name]);
      if (children.length > 0) result.push({ ...item, item: children });
    }
  }
  return result;
}

const preferredFolderOrder = [
  'Health',
  'Dev',
  'Auth',
  'Reference data',
  'Users',
  'Customers',
  'Jobs',
  'Worksheets',
  'Notifications and push',
  'Operations',
];

const filtered = filterItems(collection.item ?? []);
const rank = new Map(preferredFolderOrder.map((name, index) => [name.toLowerCase(), index]));
filtered.sort((left, right) => {
  const leftRank = rank.get(String(left.name).toLowerCase()) ?? Number.MAX_SAFE_INTEGER;
  const rightRank = rank.get(String(right.name).toLowerCase()) ?? Number.MAX_SAFE_INTEGER;
  return leftRank - rightRank;
});

collection.info = {
  ...collection.info,
  name: `${collection.info?.name ?? 'Workslip API'} - isolated CI`,
  description: `${collection.info?.description ?? ''}\n\nGenerated for an isolated GitHub Actions stack. External-provider requests are deliberately excluded.`,
};
collection.item = filtered;

fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, `${JSON.stringify(collection, null, 2)}\n`);

console.log(`CI Postman collection written to ${output}`);
console.log(`Retained requests: ${retainedRequests}`);
console.log(`Excluded requests: ${skipped.length}`);
for (const item of skipped) {
  console.log(`- ${item.method} ${item.url} (${item.reason})`);
}
