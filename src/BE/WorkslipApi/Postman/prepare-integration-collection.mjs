import fs from 'node:fs';

const [sourcePath, targetPath] = process.argv.slice(2);
if (!sourcePath || !targetPath) {
  console.error('Usage: node prepare-integration-collection.mjs <source> <target>');
  process.exit(64);
}

const collection = JSON.parse(fs.readFileSync(sourcePath, 'utf8'));
const jobsFolder = collection.item?.find((item) => item.name === 'Jobs');
const createJobRequest = jobsFolder?.item?.find(
  (item) => item.name === '/api/jobs' && item.request?.method === 'POST',
);

if (!createJobRequest?.request?.body?.raw) {
  throw new Error('Could not find the canonical POST /api/jobs integration fixture.');
}

const payload = JSON.parse(createJobRequest.request.body.raw);
if (!Array.isArray(payload.timesheets) || payload.timesheets.length === 0) {
  payload.timesheets = [
    {
      workDate: '2026-10-01',
      userId: '{{userId}}',
      hoursWorked: 1,
      sleptOnJob: false,
    },
  ];
}

createJobRequest.request.body.raw = JSON.stringify(payload, null, 2);
fs.writeFileSync(targetPath, `${JSON.stringify(collection, null, 2)}\n`, 'utf8');
