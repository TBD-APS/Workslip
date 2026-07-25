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
  {
    method: 'POST',
    pattern: /\/api\/users\/?(?:\?|$)/i,
    namePattern: /^Users \/ \/api\/users$/i,
    reason: 'valid user provisioning requires Microsoft Graph',
  },
  {
    method: 'DELETE',
    pattern: /\/api\/users\//i,
    reason: 'requires a separately provisioned disposable user rather than deleting the test actor',
  },
];

function clone(value) {
  return JSON.parse(JSON.stringify(value));
}

function rawUrl(request) {
  const raw = request?.url?.raw;
  if (typeof raw === 'string') return raw;
  if (typeof request?.url === 'string') return request.url;
  return '';
}

function normalizeUrl(request) {
  if (request?.url && typeof request.url === 'object' && rawUrl(request).startsWith('{{baseUrl}}')) {
    request.url.host = ['{{baseUrl}}'];
  }
}

function setHeader(request, key, value) {
  request.header ??= [];
  const existing = request.header.find(header => String(header.key).toLowerCase() === key.toLowerCase());
  if (existing) existing.value = value;
  else request.header.push({ key, value });
}

function removeHeader(request, key) {
  request.header = (request.header ?? []).filter(header => String(header.key).toLowerCase() !== key.toLowerCase());
}

function testEvent(item) {
  item.event ??= [];
  let event = item.event.find(candidate => candidate.listen === 'test');
  if (!event) {
    event = { listen: 'test', script: { type: 'text/javascript', exec: [] } };
    item.event.push(event);
  }
  event.script ??= { type: 'text/javascript', exec: [] };
  event.script.exec ??= [];
  return event;
}

function setTests(item, lines) {
  testEvent(item).script = { type: 'text/javascript', exec: lines };
}

function addIdempotencyHeader(item) {
  const method = String(item.request?.method ?? '').toUpperCase();
  const url = rawUrl(item.request);
  const requiresKey =
    (method === 'POST' && /\/api\/customers\/?(?:\?|$)/i.test(url)) ||
    (method === 'POST' && /\/api\/jobs\/?(?:\?|$)/i.test(url)) ||
    (method === 'PATCH' && /\/api\/jobs\/\{\{jobId\}\}(?:\?|$)/i.test(url)) ||
    (method === 'POST' && /\/api\/jobs\/\{\{jobId\}\}\/status(?:\?|$)/i.test(url));
  if (requiresKey) setHeader(item.request, 'Idempotency-Key', 'ci-{{$guid}}');
}

function patchRequest(item, fullName) {
  const method = String(item.request?.method ?? '').toUpperCase();
  const url = rawUrl(item.request);
  normalizeUrl(item.request);
  addIdempotencyHeader(item);

  if (fullName === 'Customers / /api/customers' && method === 'GET') {
    setTests(item, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.test('customers list returned', function () { pm.expect(json.items).to.be.an('array'); pm.expect(json.totalCount).to.be.a('number'); });",
      "pm.test('customer view model hides internal timestamps', function () { if (json.items.length > 0) ['createdAt', 'updatedAt'].forEach(field => pm.expect(json.items[0]).to.not.have.property(field)); });",
      "if (json.items.length > 0) pm.collectionVariables.set('customerId', json.items[0].id);",
    ]);
  }

  if (fullName === 'Customers / /api/customers/{id} (delete)' && method === 'DELETE') {
    setTests(item, [
      "pm.test('204 No Content or 404 Not Found', function () { pm.expect([204, 404]).to.include(pm.response.code); });",
    ]);
  }

  if (fullName === 'Jobs / /api/jobs?customerNameSearch' && method === 'GET') {
    setTests(item, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.test('response has items and totalCount', function () { pm.expect(json.items).to.be.an('array'); pm.expect(json.totalCount).to.be.a('number'); });",
    ]);
  }

  if (fullName === 'Invites / /api/auth/invites' && method === 'GET') {
    setTests(item, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.test('returns invite list', function () { pm.expect(json.invites).to.be.an('array'); });",
      'if (json.invites.length > 0) {',
      "  pm.test('invite items have required fields', function () {",
      '    json.invites.forEach(function(invite) {',
      "      pm.expect(invite.email).to.be.a('string');",
      "      pm.expect(invite.status).to.be.a('string');",
      "      pm.expect(invite.createdAt).to.be.a('string');",
      '    });',
      '  });',
      '}',
    ]);
  }

  if (method === 'POST' && /\/api\/jobs\/?(?:\?|$)/i.test(url)) {
    item.event = (item.event ?? []).filter(event => event.listen !== 'prerequest');
  }

  if (method === 'POST' && /\/api\/jobs\/\{\{jobId\}\}\/status(?:\?|$)/i.test(url)) {
    const body = item.request?.body?.raw;
    if (typeof body === 'string') item.request.body.raw = body.replace('"Submitted"', '"InReview"');
  }

  if (fullName === 'Jobs / /api/jobs/{id}/assign' && method === 'POST') {
    item.request.body.raw = JSON.stringify({ userIds: ['{{userId}}'] }, null, 2);
    setTests(item, [
      "pm.test('200 OK or 404 Not Found', function () { pm.expect([200, 404]).to.include(pm.response.code); });",
      'if (pm.response.code === 200) {',
      '  const json = pm.response.json();',
      "  pm.test('test actor assigned', function () {",
      "    pm.expect(json.assignedUsers).to.be.an('array');",
      "    pm.expect(json.assignedUsers.map(user => user.id)).to.include(pm.variables.get('userId'));",
      '  });',
      "  pm.test('legacy assignedUser removed', function () { pm.expect(json).to.not.have.property('assignedUser'); });",
      '}',
    ]);
  }
}

const skipped = [];
let retainedRequests = 0;

function filterItems(items, parents = []) {
  const result = [];
  for (const item of items ?? []) {
    if (item.request) {
      const method = String(item.request.method ?? 'GET').toUpperCase();
      const url = rawUrl(item.request);
      const fullName = [...parents, item.name].join(' / ');
      const rule = excluded.find(entry =>
        entry.method === method &&
        entry.pattern.test(url) &&
        (!entry.namePattern || entry.namePattern.test(fullName)));
      if (rule) {
        skipped.push({ name: fullName, method, url, reason: rule.reason });
        continue;
      }
      patchRequest(item, fullName);
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

function prepareCollectionVariables() {
  collection.event ??= [];
  let prerequest = collection.event.find(event => event.listen === 'prerequest');
  if (!prerequest) {
    prerequest = { listen: 'prerequest', script: { type: 'text/javascript', exec: [] } };
    collection.event.push(prerequest);
  }
  prerequest.script ??= { type: 'text/javascript', exec: [] };
  prerequest.script.exec ??= [];
  prerequest.script.exec.push(
    "if (!pm.collectionVariables.get('targetReportNumber')) {",
    "  const targetRunId = pm.collectionVariables.get('runId') || Date.now().toString();",
    "  pm.collectionVariables.set('targetReportNumber', `WS-IT-LINK-${targetRunId}`);",
    '}',
    "if (!pm.collectionVariables.get('creatorUserId')) pm.collectionVariables.set('creatorUserId', pm.variables.get('userId'));",
    "if (!pm.collectionVariables.get('assigneeUserId')) pm.collectionVariables.set('assigneeUserId', pm.variables.get('userId'));",
  );
}

function prepareCustomers(folder) {
  const rank = item => {
    const method = String(item.request?.method ?? '').toUpperCase();
    const name = item.name;
    if (method === 'POST' && name === '/api/customers (create)') return 0;
    if (method === 'GET' && name === '/api/customers') return 1;
    if (method === 'GET' && name === '/api/customers/{id}') return 2;
    if (method === 'GET' && name === '/api/customers/search') return 3;
    if (method === 'GET' && name === '/api/customers/suggest') return 4;
    if (method === 'GET' && name === '/api/customers/top') return 5;
    if (method === 'PUT') return 6;
    if (method === 'POST' && name === '/api/customers/import') return 7;
    if (method === 'DELETE') return 8;
    return 20;
  };
  folder.item.sort((left, right) => rank(left) - rank(right));
}

function makeJobCreateWithoutReferenceSelections(item) {
  const body = JSON.parse(item.request.body.raw);
  body.work = null;
  item.request.body.raw = JSON.stringify(body, null, 2);
  setTests(item, [
    "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
    'const json = pm.response.json();',
    "pm.test('job has id', function () { pm.expect(json.id).to.be.a('string').and.not.empty; });",
    "pm.test('job summary hides internal timestamps', function () { ['createdAt', 'updatedAt', 'submittedAt', 'deletionScheduledAt'].forEach(field => pm.expect(json).to.not.have.property(field)); });",
    "pm.test('job is active draft', function () { pm.expect(json.softDeleted).to.eql(false); });",
    "pm.test('customer email echoed', function () { pm.expect(json.customer.email).to.eql(pm.variables.get('customerEmail')); });",
    "pm.test('creator auto-assigned', function () { pm.expect(json.assignedUsers.map(user => user.id)).to.include(pm.variables.get('creatorUserId')); });",
    "pm.collectionVariables.set('jobId', json.id);",
  ]);
}

function prepareJobs(folder) {
  const create = folder.item.find(item => item.name === '/api/jobs' && item.request?.method === 'POST');
  if (create) {
    makeJobCreateWithoutReferenceSelections(create);
    const target = clone(create);
    target.name = '/api/jobs target for links';
    target.request.description = 'Creates a second isolated draft job used by link tests.';
    target.request.body.raw = target.request.body.raw.replace('{{reportNumber}}', '{{targetReportNumber}}');
    setHeader(target.request, 'Idempotency-Key', 'ci-target-{{$guid}}');
    setTests(target, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.collectionVariables.set('targetJobId', json.id);",
      "pm.test('target job id captured', function () { pm.expect(json.id).to.be.a('string').and.not.empty; });",
    ]);
    folder.item.push(target);
  }

  const assigned = folder.item.find(item => item.name === '/api/jobs/my-assigned');
  if (assigned) {
    removeHeader(assigned.request, 'If-None-Match');
    assigned.request.description = 'Gets the current user assigned-job list and stores its ETag.';
    setTests(assigned, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      "pm.test('private revalidation header present', function () { pm.expect(pm.response.headers.get('Cache-Control')).to.include('private'); });",
      "const assignedEtag = pm.response.headers.get('ETag');",
      "pm.test('ETag header present', function () { pm.expect(assignedEtag).to.be.a('string').and.not.empty; });",
      "pm.collectionVariables.set('assignedJobsEtag', assignedEtag);",
    ]);

    const revalidate = clone(assigned);
    revalidate.name = '/api/jobs/my-assigned with If-None-Match';
    setHeader(revalidate.request, 'If-None-Match', '{{assignedJobsEtag}}');
    revalidate.request.description = 'Revalidates the assigned-job list and expects 304.';
    setTests(revalidate, [
      "pm.test('304 Not Modified', function () { pm.response.to.have.status(304); });",
      "pm.test('private revalidation header present', function () { pm.expect(pm.response.headers.get('Cache-Control')).to.include('private'); });",
    ]);
    folder.item.push(revalidate);
  }

  const cleanup = [];
  const deferred = [];
  folder.item = folder.item.filter(item => {
    const method = String(item.request?.method ?? '').toUpperCase();
    if (method === 'POST' && item.name === '/api/jobs/{id}/status') {
      deferred.push(item);
      return false;
    }
    if (method === 'DELETE' && item.name === '/api/jobs/{id}') {
      cleanup.push(item);
      return false;
    }
    if (method === 'POST' && item.name === '/api/jobs/{id}/restore/deletion') {
      cleanup.push(item);
      return false;
    }
    return true;
  });

  const rank = item => {
    const method = String(item.request?.method ?? '').toUpperCase();
    const name = item.name;
    if (method === 'POST' && name === '/api/jobs') return 0;
    if (method === 'POST' && name.includes('duplicate report number')) return 1;
    if (name === '/api/jobs target for links') return 2;
    if (method === 'GET' && name === '/api/jobs') return 3;
    if (name === '/api/jobs/my-assigned') return 4;
    if (name === '/api/jobs/my-assigned with If-None-Match') return 5;
    if (method === 'GET' && name === '/api/jobs/{id}') return 6;
    if (name.includes('/history')) return 7;
    if (name.includes('/report/pdf')) return 8;
    if (name.includes('with If-None-Match')) return 9;
    if (method === 'PATCH') return 10;
    if (method === 'POST' && name === '/api/jobs/{id}/links') return 11;
    if (method === 'DELETE' && name.includes('links batch delete')) return 12;
    if (name.includes('/assign')) return 13;
    if (name.includes('customerNameSearch')) return 14;
    return 30;
  };
  folder.item.sort((left, right) => rank(left) - rank(right));
  return { cleanup, deferred };
}

prepareCollectionVariables();
const filtered = filterItems(collection.item ?? []);
let cleanupItems = [];
let deferredItems = [];
for (const folder of filtered) {
  if (folder.name === 'Customers') prepareCustomers(folder);
  if (folder.name === 'Jobs') {
    const prepared = prepareJobs(folder);
    cleanupItems = prepared.cleanup;
    deferredItems = prepared.deferred;
  }
}
if (deferredItems.length > 0) filtered.push({ name: 'Deferred mutations', item: deferredItems });
if (cleanupItems.length > 0) filtered.push({ name: 'Cleanup', item: cleanupItems });

const preferredFolderOrder = [
  'Health',
  'Dev',
  'Auth',
  'Reference Data',
  'Reference data',
  'Users',
  'Customers',
  'Jobs',
  'Worksheets',
  'Notifications and push',
  'Invites',
  'Operations',
  'Deferred mutations',
  'Cleanup',
];
const folderRank = new Map(preferredFolderOrder.map((name, index) => [name.toLowerCase(), index]));
filtered.sort((left, right) =>
  (folderRank.get(String(left.name).toLowerCase()) ?? Number.MAX_SAFE_INTEGER) -
  (folderRank.get(String(right.name).toLowerCase()) ?? Number.MAX_SAFE_INTEGER));

collection.info = {
  ...collection.info,
  name: `${collection.info?.name ?? 'Workslip API'} - isolated CI`,
  description: `${collection.info?.description ?? ''}\n\nGenerated for an isolated GitHub Actions stack. External-provider requests are deliberately excluded and stateful requests are ordered for a disposable database.`,
};
collection.item = filtered;

fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, `${JSON.stringify(collection, null, 2)}\n`);

console.log(`CI Postman collection written to ${output}`);
console.log(`Retained source requests: ${retainedRequests}`);
console.log(`Generated requests: ${retainedRequests + 2}`);
console.log(`Excluded requests: ${skipped.length}`);
for (const item of skipped) {
  console.log(`- ${item.method} ${item.url} (${item.reason})`);
}
