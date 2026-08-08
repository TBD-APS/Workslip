import assert from 'node:assert/strict';
import test from 'node:test';
import { findNewErrors } from './compare-eslint-results.mjs';

function result(filePath, source, messages) {
  return [{
    filePath,
    source,
    errorCount: messages.filter((message) => message.severity === 2).length,
    warningCount: messages.filter((message) => message.severity === 1).length,
    messages,
  }];
}

const existingError = {
  severity: 2,
  ruleId: 'react-hooks/refs',
  message: 'Cannot access refs during render',
  line: 2,
  endLine: 2,
};

test('allows existing errors even when their line number moves', () => {
  const baseline = result('/tmp/base/src/FE/src/example.tsx', 'const before = 1;\nreadRef();', [existingError]);
  const current = result('/tmp/head/src/FE/src/example.tsx', 'const inserted = 1;\nconst before = 1;\nreadRef();', [
    { ...existingError, line: 3, endLine: 3 },
  ]);

  assert.deepEqual(findNewErrors(baseline, current), []);
});

test('blocks a new error while ignoring new warnings', () => {
  const baseline = result('/tmp/base/src/FE/src/example.tsx', 'const clean = true;', []);
  const current = result('/tmp/head/src/FE/src/example.tsx', 'setState(true);\nconst warning = true;', [
    {
      severity: 2,
      ruleId: 'react-hooks/set-state-in-effect',
      message: 'Calling setState synchronously within an effect can trigger cascading renders',
      line: 1,
      endLine: 1,
    },
    {
      severity: 1,
      ruleId: 'react-hooks/exhaustive-deps',
      message: 'React Hook has a missing dependency',
      line: 2,
      endLine: 2,
    },
  ]);

  assert.deepEqual(findNewErrors(baseline, current), [{
    filePath: 'src/example.tsx',
    ruleId: 'react-hooks/set-state-in-effect',
    message: 'Calling setState synchronously within an effect can trigger cascading renders',
    line: 1,
    count: 1,
  }]);
});

test('blocks additional occurrences of the same existing error', () => {
  const baseline = result('/tmp/base/src/FE/src/example.tsx', 'readRef();', [
    { ...existingError, line: 1, endLine: 1 },
  ]);
  const current = result('/tmp/head/src/FE/src/example.tsx', 'readRef();\nreadRef();', [
    { ...existingError, line: 1, endLine: 1 },
    { ...existingError, line: 2, endLine: 2 },
  ]);

  const additions = findNewErrors(baseline, current);
  assert.equal(additions.length, 1);
  assert.equal(additions[0].count, 1);
});
