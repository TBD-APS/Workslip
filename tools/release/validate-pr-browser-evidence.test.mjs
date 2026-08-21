import assert from 'node:assert/strict';
import test from 'node:test';

import {
  inferBrowserFlows,
  isUiRuntimePath,
  parseBrowserScriptMappings,
  parseEvidence,
  registeredBrowserScripts,
  validateBrowserEvidence,
} from './validate-pr-browser-evidence.mjs';

const RUNNER = `
run_scenario 'job lifecycle' scripts/playwright-critical-job-lifecycle.mjs
run_scenario 'notifications' scripts/playwright-shared-state-semantics.mjs
run_scenario 'shared UI' scripts/playwright-ephemeral-smoke.mjs
`;

test('ignores tests, generated clients and non-frontend files', () => {
  assert.equal(isUiRuntimePath('src/FE/src/features/jobs/JobDetails.tsx'), true);
  assert.equal(isUiRuntimePath('src/FE/src/features/jobs/JobDetails.test.tsx'), false);
  assert.equal(isUiRuntimePath('src/FE/src/api/model/jobResponse.ts'), false);
  assert.equal(isUiRuntimePath('Docs/agents/VALIDATION.md'), false);
});

test('infers named critical flows from changed runtime paths', () => {
  const flows = inferBrowserFlows([
    'src/FE/src/features/jobs/JobWizard.tsx',
    'src/FE/src/components/ActivityFeed.tsx',
  ]);

  assert.deepEqual(flows, ['job-wizard', 'notifications']);
});

test('falls back to shared-ui for generic runtime UI', () => {
  assert.deepEqual(
    inferBrowserFlows(['src/FE/src/components/common/Button.tsx']),
    ['shared-ui'],
  );
});

test('parses stable intent fields and tolerates legacy runtime fields', () => {
  const evidence = parseEvidence(`
Browser-Evidence: required
Browser-Scenarios: auth-session, shared-ui
Browser-Scripts: auth-session=playwright-critical-job-lifecycle.mjs, shared-ui=playwright-ephemeral-smoke.mjs
Browser-Viewports: desktop-1440, mobile-390
Browser-Result: passed
Browser-Page-Errors: 0
Browser-Console-Errors: 0
`);

  assert.equal(evidence.evidence, 'required');
  assert.deepEqual(evidence.scenarios, ['auth-session', 'shared-ui']);
  assert.deepEqual(evidence.scriptMappings, [
    { raw: 'auth-session=playwright-critical-job-lifecycle.mjs', flow: 'auth-session', script: 'playwright-critical-job-lifecycle.mjs' },
    { raw: 'shared-ui=playwright-ephemeral-smoke.mjs', flow: 'shared-ui', script: 'playwright-ephemeral-smoke.mjs' },
  ]);
  assert.deepEqual(evidence.viewports, ['desktop-1440', 'mobile-390']);
  assert.equal(evidence.result, 'passed');
});

test('parses registered Playwright scripts from the exact-head runner', () => {
  assert.deepEqual(
    [...registeredBrowserScripts(RUNNER)].sort(),
    [
      'playwright-critical-job-lifecycle.mjs',
      'playwright-ephemeral-smoke.mjs',
      'playwright-shared-state-semantics.mjs',
    ],
  );
});

test('rejects malformed browser script mappings', () => {
  assert.deepEqual(parseBrowserScriptMappings('job-wizard'), [
    { raw: 'job-wizard', flow: '', script: '' },
  ]);
});

test('non-UI change needs no browser declaration', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['Docs/agents/VALIDATION.md'],
    body: '',
    runnerSource: RUNNER,
  });

  assert.equal(result.required, false);
  assert.deepEqual(result.errors, []);
});

test('UI change fails closed when declaration is missing', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: '',
    runnerSource: RUNNER,
  });

  assert.equal(result.required, true);
  assert.ok(result.errors.some((error) => error.includes('Browser-Evidence')));
});

test('runtime result is not manually merge-gating state', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: job-wizard
Browser-Scripts: job-wizard=playwright-critical-job-lifecycle.mjs
Browser-Result: pending
Browser-Viewports: desktop-1440, mobile-390
Browser-Page-Errors: pending
Browser-Console-Errors: pending
`,
    runnerSource: RUNNER,
  });

  assert.deepEqual(result.errors, []);
});

test('generic browser smoke cannot satisfy a named critical flow declaration', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: generic-smoke
Browser-Scripts: generic-smoke=playwright-ephemeral-smoke.mjs
Browser-Viewports: desktop-1440
`,
    runnerSource: RUNNER,
  });

  assert.ok(result.errors.some((error) => error.includes('job-wizard')));
  assert.ok(result.errors.some((error) => error.includes('Browser-Scripts mapping for inferred flow: job-wizard')));
});

test('complete named browser intent passes when each flow maps to a registered runner script', () => {
  const result = validateBrowserEvidence({
    changedPaths: [
      'src/FE/src/features/jobs/JobWizard.tsx',
      'src/FE/src/components/ActivityFeed.tsx',
    ],
    body: `
Browser-Evidence: required
Browser-Scenarios: job-wizard, notifications
Browser-Scripts: job-wizard=playwright-critical-job-lifecycle.mjs, notifications=playwright-shared-state-semantics.mjs
Browser-Viewports: desktop-1440, mobile-390
`,
    runnerSource: RUNNER,
  });

  assert.deepEqual(result.errors, []);
});

test('required browser intent must include viewports', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/components/common/Button.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: shared-ui
Browser-Scripts: shared-ui=playwright-ephemeral-smoke.mjs
`,
    runnerSource: RUNNER,
  });

  assert.ok(result.errors.some((error) => error.includes('Browser-Viewports')));
});

test('required browser intent must map each inferred flow to a script', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: job-wizard
Browser-Viewports: desktop-1440
`,
    runnerSource: RUNNER,
  });

  assert.ok(result.errors.some((error) => error.includes('Browser-Scripts')));
  assert.ok(result.errors.some((error) => error.includes('job-wizard')));
});

test('declared browser script must actually be registered in the exact-head runner', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: job-wizard
Browser-Scripts: job-wizard=playwright-job-wizard.mjs
Browser-Viewports: desktop-1440
`,
    runnerSource: RUNNER,
  });

  assert.ok(result.errors.some((error) => error.includes('not registered')));
});

test('invalid browser script names are rejected', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: job-wizard
Browser-Scripts: job-wizard=../playwright-job-wizard.mjs
Browser-Viewports: desktop-1440
`,
    runnerSource: RUNNER,
  });

  assert.ok(result.errors.some((error) => error.includes('Invalid Playwright script name')));
});

test('waived evidence is rejected: there is no exemption path', () => {
  const waived = validateBrowserEvidence({
    changedPaths: ['src/FE/src/components/common/Button.tsx'],
    body: `
Browser-Evidence: waived
Browser-Waiver-Owner: @rasm105k
Browser-Waiver-Reason: Pure copy-only visual change; no interaction or responsive behavior changed.
`,
    runnerSource: RUNNER,
  });
  assert.ok(
    waived.errors.some((error) => error.includes('Browser-Evidence: required')),
    'A waived declaration must not satisfy the guard.',
  );

  const omitted = validateBrowserEvidence({
    changedPaths: ['src/FE/src/components/common/Button.tsx'],
    body: 'No evidence block at all.',
    runnerSource: RUNNER,
  });
  assert.ok(omitted.errors.length > 0, 'Omitting the evidence block must stay red.');
});