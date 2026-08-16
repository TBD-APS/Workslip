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

const createJobPrerequest = createJobRequest.event
  ?.find((event) => event.listen === 'prerequest')
  ?.script?.exec;
if (!Array.isArray(createJobPrerequest)) {
  throw new Error('Could not find the canonical POST /api/jobs pre-request script.');
}

// Patch the synthetic worksheet at request runtime. The canonical job pre-request
// already resolves reference-data IDs asynchronously, so mutating the static raw
// JSON during collection preparation would compete with that established builder.
// actorUserId is already known before the Jobs folder starts and is written as a
// concrete GUID while the existing {{vand...}} variables remain available for the
// original reference-data script to resolve before the request is sent.
createJobPrerequest.unshift(
  "const workslipSubmitReadyActorUserId = pm.collectionVariables.get('actorUserId');",
  "if (!workslipSubmitReadyActorUserId) { throw new Error('Missing actorUserId before POST /api/jobs'); }",
  "const workslipSubmitReadyPayload = JSON.parse(pm.request.body.raw);",
  "workslipSubmitReadyPayload.timesheets = [{ workDate: '2026-10-01', userId: workslipSubmitReadyActorUserId, hoursWorked: 1, sleptOnJob: false }];",
  "pm.request.body.update(JSON.stringify(workslipSubmitReadyPayload));",
);

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
  "  pm.collectionVariables.set('actorUserId', json.user.userId);",
  "  pm.collectionVariables.set('actorRole', json.user.role);",
);

fs.writeFileSync(targetPath, `${JSON.stringify(collection, null, 2)}\n`, 'utf8');
