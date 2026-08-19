import assert from 'node:assert/strict';
import test from 'node:test';

import {
  inferBrowserFlows,
  isUiRuntimePath,
  parseEvidence,
  validateBrowserEvidence,
} from './validate-pr-browser-evidence.mjs';

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
Browser-Viewports: desktop-1440, mobile-390
Browser-Result: passed
Browser-Page-Errors: 0
Browser-Console-Errors: 0
`);

  assert.equal(evidence.evidence, 'required');
  assert.deepEqual(evidence.scenarios, ['auth-session', 'shared-ui']);
  assert.deepEqual(evidence.viewports, ['desktop-1440', 'mobile-390']);
  assert.equal(evidence.result, 'passed');
});

test('non-UI change needs no browser declaration', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['Docs/agents/VALIDATION.md'],
    body: '',
  });

  assert.equal(result.required, false);
  assert.deepEqual(result.errors, []);
});

test('UI change fails closed when declaration is missing', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: '',
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
Browser-Result: pending
Browser-Viewports: desktop-1440, mobile-390
Browser-Page-Errors: pending
Browser-Console-Errors: pending
`,
  });

  assert.deepEqual(result.errors, []);
});

test('generic browser smoke cannot satisfy a named critical flow declaration', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: generic-smoke
Browser-Viewports: desktop-1440
`,
  });

  assert.ok(result.errors.some((error) => error.includes('job-wizard')));
});

test('complete named browser intent passes without mutable status fields', () => {
  const result = validateBrowserEvidence({
    changedPaths: [
      'src/FE/src/features/jobs/JobWizard.tsx',
      'src/FE/src/components/ActivityFeed.tsx',
    ],
    body: `
Browser-Evidence: required
Browser-Scenarios: job-wizard, notifications
Browser-Viewports: desktop-1440, mobile-390
`,
  });

  assert.deepEqual(result.errors, []);
});

test('required browser intent must include viewports', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/components/common/Button.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: shared-ui
`,
  });

  assert.ok(result.errors.some((error) => error.includes('Browser-Viewports')));
});

test('waived evidence is rejected: there is no exemption path', () => {
  const waived = validateBrowserEvidence({
    changedPaths: ['src/FE/src/components/common/Button.tsx'],
    body: `
Browser-Evidence: waived
Browser-Waiver-Owner: @rasm105k
Browser-Waiver-Reason: Pure copy-only visual change; no interaction or responsive behavior changed.
`,
  });
  assert.ok(
    waived.errors.some((error) => error.includes('Browser-Evidence: required')),
    'A waived declaration must not satisfy the guard.',
  );

  const omitted = validateBrowserEvidence({
    changedPaths: ['src/FE/src/components/common/Button.tsx'],
    body: 'No evidence block at all.',
  });
  assert.ok(omitted.errors.length > 0, 'Omitting the evidence block must stay red.');
});