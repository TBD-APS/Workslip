import assert from 'node:assert/strict';
import test from 'node:test';
import { validateReleaseRunEnvironment } from './playwright-release-runner.mjs';

const preliveProduction = {
  SCENARIO: 'public-smoke',
  WORKSLIP_RELEASE_PHASE: 'prelive',
  WORKSLIP_TEST_TARGET: 'production',
  WORKSLIP_ALLOW_DESTRUCTIVE_PLAYWRIGHT: 'false',
  PROD_URL: 'https://app.example.test',
};

test('pre-live production permits the public smoke only', () => {
  assert.deepEqual(validateReleaseRunEnvironment(preliveProduction), {
    scenario: 'public-smoke',
    phase: 'prelive',
    target: 'production',
    allowDestructive: false,
    appUrl: 'https://app.example.test',
    isPublicSmoke: true,
  });
});

test('production rejects every authenticated or destructive scenario in every phase', () => {
  assert.throws(
    () => validateReleaseRunEnvironment({
      ...preliveProduction,
      SCENARIO: 'assignment-lifecycle',
      WORKSLIP_ALLOW_DESTRUCTIVE_PLAYWRIGHT: 'true',
    }),
    /Production permits only the write-free public-smoke scenario/,
  );
});

test('a critical scenario requires a configured live staging target', () => {
  assert.throws(
    () => validateReleaseRunEnvironment({
      ...preliveProduction,
      SCENARIO: 'assignment-lifecycle',
      WORKSLIP_TEST_TARGET: 'staging',
      WORKSLIP_ALLOW_DESTRUCTIVE_PLAYWRIGHT: 'true',
    }),
    /not configured before the isolated live-phase target exists/,
  );
});

test('critical staging execution fails closed without destructive permission', () => {
  assert.throws(
    () => validateReleaseRunEnvironment({
      ...preliveProduction,
      SCENARIO: 'assignment-lifecycle',
      WORKSLIP_RELEASE_PHASE: 'live',
      WORKSLIP_TEST_TARGET: 'staging',
    }),
    /requires an authenticated isolated release-test environment/,
  );
});

test('release target URLs must be clean HTTPS origins', () => {
  assert.throws(
    () => validateReleaseRunEnvironment({
      ...preliveProduction,
      PROD_URL: 'https://app.example.test/login?token=unsafe',
    }),
    /configured HTTPS origin without credentials, path, query, or fragment/,
  );
});
