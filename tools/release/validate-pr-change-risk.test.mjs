import assert from 'node:assert/strict';
import test from 'node:test';

import {
  classifyChangeRisk,
  parseNameStatus,
  parseNumstat,
} from './validate-pr-change-risk.mjs';

function classify(numstatText, nameStatusText) {
  return classifyChangeRisk({
    numstat: parseNumstat(numstatText),
    nameStatus: parseNameStatus(nameStatusText),
  });
}

test('small product refactor is not high risk', () => {
  const result = classify(
    '80\t60\tsrc/FE/src/features/jobs/JobDetails.tsx\n',
    'M\tsrc/FE/src/features/jobs/JobDetails.tsx\n',
  );

  assert.equal(result.highRisk, false);
  assert.equal(result.productDeleted, 60);
});

test('documentation cleanup does not trigger product feature guard', () => {
  const result = classify(
    '10\t5000\tDocs/old-feature-plan.md\n',
    'D\tDocs/old-feature-plan.md\n',
  );

  assert.equal(result.highRisk, false);
  assert.equal(result.productDeleted, 0);
});

test('three deleted product files trigger high risk', () => {
  const result = classify(
    [
      '0\t80\tsrc/FE/src/features/jobs/a.tsx',
      '0\t60\tsrc/FE/src/features/jobs/b.tsx',
      '0\t40\tsrc/BE/WorkslipApi/Endpoints/ImageEndpoints.cs',
    ].join('\n'),
    [
      'D\tsrc/FE/src/features/jobs/a.tsx',
      'D\tsrc/FE/src/features/jobs/b.tsx',
      'D\tsrc/BE/WorkslipApi/Endpoints/ImageEndpoints.cs',
    ].join('\n'),
  );

  assert.equal(result.highRisk, true);
  assert.equal(result.productDeletedFiles.length, 3);
});

test('large deletion-dominant product rewrite triggers high risk', () => {
  const result = classify(
    '100\t700\tsrc/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs\n',
    'M\tsrc/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs\n',
  );

  assert.equal(result.highRisk, true);
  assert.ok(result.reasons.some((reason) => reason.includes('700 product-code lines')));
});

test('balanced large refactor is allowed', () => {
  const result = classify(
    '650\t500\tsrc/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs\n',
    'M\tsrc/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs\n',
  );

  assert.equal(result.highRisk, false);
});

test('cross-feature deletion can trigger even below global line threshold', () => {
  const result = classify(
    [
      '80\t160\tsrc/FE/src/features/jobs/a.tsx',
      '40\t140\tsrc/FE/src/features/customers/b.tsx',
    ].join('\n'),
    [
      'M\tsrc/FE/src/features/jobs/a.tsx',
      'M\tsrc/FE/src/features/customers/b.tsx',
    ].join('\n'),
  );

  assert.equal(result.highRisk, true);
  assert.equal(result.featureAreas.length, 2);
});

test('rename is not treated as deleted product file', () => {
  const result = classify(
    '10\t10\tsrc/FE/src/features/jobs/NewName.tsx\n',
    'R100\tsrc/FE/src/features/jobs/OldName.tsx\tsrc/FE/src/features/jobs/NewName.tsx\n',
  );

  assert.equal(result.highRisk, false);
  assert.deepEqual(result.productDeletedFiles, []);
});
