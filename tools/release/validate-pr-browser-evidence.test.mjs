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

test('parses machine-readable evidence fields', () => {
  const evidence = parseEvidence(`
Browser-Evidence: required
Browser-Scenarios: auth-session, shared-ui
Browser-Result: passed
Browser-Viewports: desktop-1440, mobile-390
Browser-Page-Errors: 0
Browser-Console-Errors: 0
`);

  assert.equal(evidence.evidence, 'required');
  assert.deepEqual(evidence.scenarios, ['auth-session', 'shared-ui']);
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

test('pending required evidence blocks merge-readiness', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: job-wizard
Browser-Result: pending
Browser-Viewports: desktop-1440, mobile-390
Browser-Page-Errors: 0
Browser-Console-Errors: 0
`,
  });

  assert.ok(result.errors.some((error) => error.includes('Browser-Result: passed')));
});

test('generic browser smoke cannot satisfy a named critical flow', () => {
  const result = validateBrowserEvidence({
    changedPaths: ['src/FE/src/features/jobs/JobWizard.tsx'],
    body: `
Browser-Evidence: required
Browser-Scenarios: generic-smoke
Browser-Result: passed
Browser-Viewports: desktop-1440
Browser-Page-Errors: 0
Browser-Console-Errors: 0
`,
  });

  assert.ok(result.errors.some((error) => error.includes('job-wizard')));
});

test('complete named evidence passes', () => {
  const result = validateBrowserEvidence({
    changedPaths: [
      'src/FE/src/features/jobs/JobWizard.tsx',
      'src/FE/src/components/ActivityFeed.tsx',
    ],
    body: `
Browser-Evidence: required
Browser-Scenarios: job-wizard, notifications
Browser-Result: passed
Browser-Viewports: desktop-1440, mobile-390
Browser-Page-Errors: 0
Browser-Console-Errors: 0
`,
  });

  assert.deepEqual(result.errors, []);
});

test('explicit waiver requires owner and concrete reason', () => {
  const incomplete = validateBrowserEvidence({
    changedPaths: ['src/FE/src/components/common/Button.tsx'],
    body: `
Browser-Evidence: waived
Browser-Waiver-Owner: @rasm105k
Browser-Waiver-Reason: short
`,
  });
  assert.ok(incomplete.errors.length > 0);

  const accepted = validateBrowserEvidence({
    changedPaths: ['src/FE/src/components/common/Button.tsx'],
    body: `
Browser-Evidence: waived
Browser-Waiver-Owner: @rasm105k
Browser-Waiver-Reason: Pure copy-only visual change; no interaction or responsive behavior changed.
`,
  });
  assert.deepEqual(accepted.errors, []);
});
