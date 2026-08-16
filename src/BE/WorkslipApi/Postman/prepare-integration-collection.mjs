import fs from 'node:fs';

const [sourcePath, targetPath] = process.argv.slice(2);
if (!sourcePath || !targetPath) {
  console.error('Usage: node prepare-integration-collection.mjs <source> <target>');
  process.exit(64);
}

const bootstrapToken = process.env.WORKSLIP_AUTH_TOKEN;
if (!bootstrapToken) {
  throw new Error('WORKSLIP_AUTH_TOKEN is required to prepare the integration collection.');
}

const collection = JSON.parse(fs.readFileSync(sourcePath, 'utf8'));
const authTokenVariable = collection.variable?.find((variable) => variable.key === 'authToken');
if (!authTokenVariable) {
  throw new Error('Could not find the authToken collection variable.');
}
authTokenVariable.value = bootstrapToken;

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
      userId: '{{actorUserId}}',
      hoursWorked: 1,
      sleptOnJob: false,
    },
  ];
}
createJobRequest.request.body.raw = JSON.stringify(payload, null, 2);

const devFolder = collection.item?.find((item) => item.name === 'Dev');
const devTokenRequest = devFolder?.item?.find(
  (item) => item.name === '/api/dev/token' && item.request?.method === 'POST',
);
const devTokenTest = devTokenRequest?.event?.find((event) => event.listen === 'test')?.script?.exec;
if (!Array.isArray(devTokenTest)) {
  throw new Error('Could not find the /api/dev/token test script.');
}

const tokenCaptureIndex = devTokenTest.findIndex((line) =>
  line.includes("pm.collectionVariables.set('authToken', json.token)"),
);
if (tokenCaptureIndex < 0) {
  throw new Error('Could not find the /api/dev/token authToken capture line.');
}

devTokenTest.splice(
  tokenCaptureIndex + 1,
  0,
  "  pm.collectionVariables.set('actorUserId', json.user.id);",
  "  pm.collectionVariables.set('actorRole', json.user.role);",
);

fs.writeFileSync(targetPath, `${JSON.stringify(collection, null, 2)}\n`, 'utf8');
