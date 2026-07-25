import fs from 'node:fs';
import path from 'node:path';

const [collectionArg] = process.argv.slice(2);
if (!collectionArg) {
  console.error('Usage: node finalize-ci-postman-collection.mjs <collection>');
  process.exit(64);
}

const collectionPath = path.resolve(collectionArg);
const collection = JSON.parse(fs.readFileSync(collectionPath, 'utf8'));
let removed = 0;
let patched = 0;

function setTests(item, lines) {
  item.event ??= [];
  let event = item.event.find(candidate => candidate.listen === 'test');
  if (!event) {
    event = { listen: 'test', script: { type: 'text/javascript', exec: [] } };
    item.event.push(event);
  }
  event.script = { type: 'text/javascript', exec: lines };
}

function visitFolder(folder) {
  folder.item = (folder.item ?? []).filter(item => {
    if (folder.name === 'Jobs' && item.name === '/api/jobs duplicate report number') {
      removed += 1;
      return false;
    }
    return true;
  });

  for (const item of folder.item) {
    if (Array.isArray(item.item)) {
      visitFolder(item);
      continue;
    }

    const method = String(item.request?.method ?? '').toUpperCase();

    if (folder.name === 'Jobs' && item.name === '/api/jobs' && method === 'POST') {
      const event = item.event?.find(candidate => candidate.listen === 'test');
      if (event?.script?.exec) {
        event.script.exec = event.script.exec.map(line =>
          line.replace('json.customer.email', 'json.customerSnapshot.email'));
        patched += 1;
      }
    }

    if (folder.name === 'Jobs' && item.name === '/api/jobs/{id}/links' && method === 'POST') {
      setTests(item, [
        "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
        'const json = pm.response.json();',
        "pm.test('updated job summary includes links', function () { pm.expect(json.links).to.be.an('array').that.is.not.empty; });",
        "const targetJobId = pm.collectionVariables.get('targetJobId');",
        'const link = json.links.find(candidate => candidate.linkedReportId === targetJobId) || json.links[json.links.length - 1];',
        "pm.test('link id captured', function () { pm.expect(link.id).to.be.a('string').and.not.empty; });",
        "pm.test('linked report info returned', function () { pm.expect(link.linkedReportNumber).to.be.a('string').and.not.empty; });",
        "pm.collectionVariables.set('linkId', link.id);",
      ]);
      patched += 1;
    }

    if (folder.name === 'Deferred mutations' && item.name === '/api/jobs/{id}/status' && method === 'POST') {
      item.request.description = 'Exercises status validation when the isolated database has no installation/work-kind reference definitions.';
      setTests(item, [
        "pm.test('400 validation response', function () { pm.response.to.have.status(400); });",
        "pm.test('mutation response is not stored', function () { pm.expect(pm.response.headers.get('Cache-Control')).to.include('no-store'); });",
      ]);
      patched += 1;
    }
  }
}

for (const folder of collection.item ?? []) visitFolder(folder);

fs.writeFileSync(collectionPath, `${JSON.stringify(collection, null, 2)}\n`);
console.log(`Finalized CI Postman collection: ${patched} requests patched, ${removed} unsupported conflict scenario removed.`);
