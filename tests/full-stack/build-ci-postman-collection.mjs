import fs from 'node:fs';
import path from 'node:path';

const [sourceArg, outputArg] = process.argv.slice(2);
if (!sourceArg || !outputArg) {
  console.error('Usage: node build-ci-postman-collection.mjs <source> <output>');
  process.exit(64);
}

const collection = JSON.parse(fs.readFileSync(path.resolve(sourceArg), 'utf8'));
const output = path.resolve(outputArg);

const exclusions = [
  ['POST', /\/api\/organizations\/?(?:\?|$)/i, null, 'created during isolated-stack bootstrap'],
  ['POST', /\/api\/auth\/send-code(?:\?|$)/i, null, 'requires external email delivery'],
  ['POST', /\/api\/auth\/verify-code\//i, null, 'requires an out-of-band email code'],
  ['POST', /\/api\/auth\/entra-(?:login|enroll)(?:\?|$)/i, null, 'requires Microsoft Entra'],
  ['POST', /\/api\/auth\/invite(?:\?|$)/i, null, 'requires external invitation delivery'],
  ['POST', /\/api\/auth\/invite\/[^/]+\/open(?:\?|$)/i, null, 'requires a real invitation token'],
  ['POST', /\/api\/push-subscriptions\/?(?:\?|$)/i, null, 'requires a browser push subscription'],
  ['POST', /\/api\/admin\/cache\/clear(?:\?|$)/i, null, 'may call external cache invalidation'],
  ['POST', /\/api\/users\/?(?:\?|$)/i, /^Users \/ \/api\/users$/i, 'valid user provisioning requires Microsoft Graph'],
  ['DELETE', /\/api\/users\//i, null, 'requires a separately provisioned disposable user'],
  ['POST', /\/api\/jobs\/?(?:\?|$)/i, /^Jobs \/ \/api\/jobs duplicate report number$/i, 'the current API allocates report numbers server-side'],
];

const skipped = [];
let retained = 0;
let generated = 0;

const clone = value => JSON.parse(JSON.stringify(value));
const methodOf = item => String(item.request?.method ?? '').toUpperCase();
const rawUrl = request => typeof request?.url === 'string' ? request.url : String(request?.url?.raw ?? '');

function setHeader(request, key, value) {
  request.header ??= [];
  const header = request.header.find(entry => String(entry.key).toLowerCase() === key.toLowerCase());
  if (header) header.value = value;
  else request.header.push({ key, value });
}

function removeHeader(request, key) {
  request.header = (request.header ?? []).filter(entry => String(entry.key).toLowerCase() !== key.toLowerCase());
}

function setTests(item, lines) {
  item.event ??= [];
  let event = item.event.find(candidate => candidate.listen === 'test');
  if (!event) {
    event = { listen: 'test', script: { type: 'text/javascript', exec: [] } };
    item.event.push(event);
  }
  event.script = { type: 'text/javascript', exec: lines };
}

function normalizeUrl(request) {
  if (request?.url && typeof request.url === 'object' && rawUrl(request).startsWith('{{baseUrl}}')) {
    request.url.host = ['{{baseUrl}}'];
  }
}

function addIdempotency(item) {
  const method = methodOf(item);
  const url = rawUrl(item.request);
  const required =
    (method === 'POST' && /\/api\/customers\/?(?:\?|$)/i.test(url)) ||
    (method === 'POST' && /\/api\/jobs\/?(?:\?|$)/i.test(url)) ||
    (method === 'PATCH' && /\/api\/jobs\/\{\{jobId\}\}(?:\?|$)/i.test(url)) ||
    (method === 'POST' && /\/api\/jobs\/\{\{jobId\}\}\/status(?:\?|$)/i.test(url));
  if (required) setHeader(item.request, 'Idempotency-Key', 'ci-{{$guid}}');
}

function patchRequest(item, fullName) {
  const method = methodOf(item);
  const url = rawUrl(item.request);
  normalizeUrl(item.request);
  addIdempotency(item);

  if (fullName === 'Customers / /api/customers' && method === 'GET') {
    setTests(item, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.test('paginated customer list returned', function () { pm.expect(json.items).to.be.an('array'); pm.expect(json.totalCount).to.be.a('number'); });",
      "pm.test('customer list hides timestamps', function () { if (json.items.length > 0) ['createdAt', 'updatedAt'].forEach(field => pm.expect(json.items[0]).to.not.have.property(field)); });",
      "if (json.items.length > 0) pm.collectionVariables.set('customerId', json.items[0].id);",
    ]);
  }

  if (fullName === 'Customers / /api/customers/{id} (delete)' && method === 'DELETE') {
    setTests(item, ["pm.test('204 No Content or 404 Not Found', function () { pm.expect([204, 404]).to.include(pm.response.code); });"]);
  }

  if (fullName === 'Jobs / /api/jobs' && method === 'POST') {
    item.event = (item.event ?? []).filter(event => event.listen !== 'prerequest');
    const body = JSON.parse(item.request.body.raw);
    body.work = null;
    item.request.body.raw = JSON.stringify(body, null, 2);
    setTests(item, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.test('job has id', function () { pm.expect(json.id).to.be.a('string').and.not.empty; });",
      "pm.test('job summary hides timestamps', function () { ['createdAt', 'updatedAt', 'submittedAt', 'deletionScheduledAt'].forEach(field => pm.expect(json).to.not.have.property(field)); });",
      "pm.test('job is active draft', function () { pm.expect(json.softDeleted).to.eql(false); });",
      "pm.test('customer snapshot email echoed', function () { pm.expect(json.customerSnapshot.email).to.eql(pm.variables.get('customerEmail')); });",
      "pm.test('creator auto-assigned', function () { pm.expect(json.assignedUsers.map(user => user.id)).to.include(pm.variables.get('creatorUserId')); });",
      "pm.collectionVariables.set('jobId', json.id);",
    ]);
  }

  if (fullName === 'Jobs / /api/jobs?customerNameSearch' && method === 'GET') {
    setTests(item, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.test('paginated job list returned', function () { pm.expect(json.items).to.be.an('array'); pm.expect(json.totalCount).to.be.a('number'); });",
    ]);
  }

  if (fullName === 'Jobs / /api/jobs/{id}/links' && method === 'POST') {
    setTests(item, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.test('updated job contains links', function () { pm.expect(json.links).to.be.an('array').that.is.not.empty; });",
      "const targetJobId = pm.collectionVariables.get('targetJobId');",
      'const link = json.links.find(candidate => candidate.linkedReportId === targetJobId) || json.links[json.links.length - 1];',
      "pm.test('link id captured', function () { pm.expect(link.id).to.be.a('string').and.not.empty; });",
      "pm.test('linked report info returned', function () { pm.expect(link.linkedReportNumber).to.be.a('string').and.not.empty; });",
      "pm.collectionVariables.set('linkId', link.id);",
    ]);
  }

  if (fullName === 'Jobs / /api/jobs/{id}/assign' && method === 'POST') {
    item.request.body.raw = JSON.stringify({ userIds: ['{{userId}}'] }, null, 2);
    setTests(item, [
      "pm.test('200 OK or 404 Not Found', function () { pm.expect([200, 404]).to.include(pm.response.code); });",
      'if (pm.response.code === 200) {',
      '  const json = pm.response.json();',
      "  pm.test('test actor assigned', function () { pm.expect(json.assignedUsers.map(user => user.id)).to.include(pm.variables.get('userId')); });",
      "  pm.test('legacy assignedUser removed', function () { pm.expect(json).to.not.have.property('assignedUser'); });",
      '}',
    ]);
  }

  if (method === 'POST' && /\/api\/jobs\/\{\{jobId\}\}\/status(?:\?|$)/i.test(url)) {
    item.request.body.raw = item.request.body.raw.replace('"Submitted"', '"InReview"');
  }

  if (fullName === 'Invites / /api/auth/invites' && method === 'GET') {
    setTests(item, [
      "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
      'const json = pm.response.json();',
      "pm.test('invite list returned', function () { pm.expect(json.invites).to.be.an('array'); });",
    ]);
  }
}

function filteredItems(items, parents = []) {
  const result = [];
  for (const item of items ?? []) {
    if (item.request) {
      const method = methodOf(item);
      const url = rawUrl(item.request);
      const fullName = [...parents, item.name].join(' / ');
      const exclusion = exclusions.find(([excludedMethod, pattern, namePattern]) =>
        excludedMethod === method && pattern.test(url) && (!namePattern || namePattern.test(fullName)));
      if (exclusion) {
        skipped.push({ method, url, name: fullName, reason: exclusion[3] });
        continue;
      }
      patchRequest(item, fullName);
      retained += 1;
      result.push(item);
      continue;
    }
    const children = filteredItems(item.item, [...parents, item.name]);
    if (children.length > 0) result.push({ ...item, item: children });
  }
  return result;
}

function ensureCollectionVariables() {
  collection.event ??= [];
  let event = collection.event.find(candidate => candidate.listen === 'prerequest');
  if (!event) {
    event = { listen: 'prerequest', script: { type: 'text/javascript', exec: [] } };
    collection.event.push(event);
  }
  event.script ??= { type: 'text/javascript', exec: [] };
  event.script.exec ??= [];
  event.script.exec.push(
    "if (!pm.collectionVariables.get('targetReportNumber')) pm.collectionVariables.set('targetReportNumber', `WS-IT-LINK-${Date.now()}`);",
    "if (!pm.collectionVariables.get('creatorUserId')) pm.collectionVariables.set('creatorUserId', pm.variables.get('userId'));",
    "if (!pm.collectionVariables.get('assigneeUserId')) pm.collectionVariables.set('assigneeUserId', pm.variables.get('userId'));",
  );
}

function prepareCustomers(folder) {
  const rank = item => {
    const method = methodOf(item);
    if (method === 'POST' && item.name === '/api/customers (create)') return 0;
    if (method === 'GET' && item.name === '/api/customers') return 1;
    if (method === 'GET' && item.name === '/api/customers/{id}') return 2;
    if (method === 'GET' && item.name === '/api/customers/search') return 3;
    if (method === 'PATCH' && item.name === '/api/customers/{id}/favorite') return 4;
    if (method === 'GET' && item.name === '/api/customers/favorite') return 5;
    if (method === 'PUT') return 6;
    if (method === 'POST' && item.name === '/api/customers/import') return 7;
    if (method === 'DELETE') return 8;
    return 20;
  };
  folder.item.sort((left, right) => rank(left) - rank(right));
}

function prepareJobs(folder) {
  const create = folder.item.find(item => item.name === '/api/jobs' && methodOf(item) === 'POST');
  if (create) {
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
    generated += 1;
  }

  const assigned = folder.item.find(item => item.name === '/api/jobs/my-assigned');
  if (assigned) {
    removeHeader(assigned.request, 'If-None-Match');
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
    setTests(revalidate, [
      "pm.test('304 Not Modified', function () { pm.response.to.have.status(304); });",
      "pm.test('private revalidation header present', function () { pm.expect(pm.response.headers.get('Cache-Control')).to.include('private'); });",
    ]);
    folder.item.push(revalidate);
    generated += 1;
  }

  const deferred = [];
  const cleanup = [];
  folder.item = folder.item.filter(item => {
    const method = methodOf(item);
    if (method === 'POST' && item.name === '/api/jobs/{id}/status') {
      item.request.description = 'Exercises status validation while isolated reference definitions are empty.';
      setTests(item, [
        "pm.test('400 validation response', function () { pm.response.to.have.status(400); });",
        "pm.test('mutation response is not stored', function () { pm.expect(pm.response.headers.get('Cache-Control')).to.include('no-store'); });",
      ]);
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
    const method = methodOf(item);
    const name = item.name;
    if (method === 'POST' && name === '/api/jobs') return 0;
    if (name === '/api/jobs target for links') return 1;
    if (method === 'GET' && name === '/api/jobs') return 2;
    if (name === '/api/jobs/my-assigned') return 3;
    if (name === '/api/jobs/my-assigned with If-None-Match') return 4;
    if (method === 'GET' && name === '/api/jobs/{id}') return 5;
    if (name.includes('/history')) return 6;
    if (name.includes('/report/pdf')) return 7;
    if (name.includes('with If-None-Match')) return 8;
    if (method === 'PATCH') return 9;
    if (method === 'POST' && name === '/api/jobs/{id}/links') return 10;
    if (method === 'DELETE' && name.includes('links batch delete')) return 11;
    if (name.includes('/assign')) return 12;
    if (name.includes('customerNameSearch')) return 13;
    return 30;
  };
  folder.item.sort((left, right) => rank(left) - rank(right));
  return { deferred, cleanup };
}

ensureCollectionVariables();
const items = filteredItems(collection.item);
let deferred = [];
let cleanup = [];
for (const folder of items) {
  if (folder.name === 'Customers') prepareCustomers(folder);
  if (folder.name === 'Jobs') ({ deferred, cleanup } = prepareJobs(folder));
}
if (deferred.length > 0) items.push({ name: 'Deferred mutations', item: deferred });
if (cleanup.length > 0) items.push({ name: 'Cleanup', item: cleanup });

const order = ['Health', 'Dev', 'Auth', 'Reference Data', 'Reference data', 'Users', 'Customers', 'Jobs', 'Worksheets', 'Notifications and push', 'Invites', 'Operations', 'Deferred mutations', 'Cleanup'];
const ranks = new Map(order.map((name, index) => [name.toLowerCase(), index]));
items.sort((left, right) =>
  (ranks.get(String(left.name).toLowerCase()) ?? 999) - (ranks.get(String(right.name).toLowerCase()) ?? 999));

collection.info = {
  ...collection.info,
  name: `${collection.info?.name ?? 'Workslip API'} - isolated CI`,
  description: `${collection.info?.description ?? ''}\n\nGenerated for an isolated GitHub Actions stack. External-provider requests are excluded and stateful requests are ordered for a disposable database.`,
};
collection.item = items;

fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, `${JSON.stringify(collection, null, 2)}\n`);
console.log(`CI collection written to ${output}`);
console.log(`Requests: ${retained} retained + ${generated} generated; ${skipped.length} excluded.`);
for (const item of skipped) console.log(`- ${item.method} ${item.url}: ${item.reason}`);
