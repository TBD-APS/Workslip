import assert from 'node:assert/strict';
import test from 'node:test';
import { validateLocalActionsEnvironment } from './playwright-local-assignment.mjs';

const valid = {
  WORKSLIP_LOCAL_APP_URL: 'http://127.0.0.1:5270',
  WORKSLIP_LOCAL_API_URL: 'http://localhost:5262',
  WORKSLIP_ALLOW_LOCAL_DEV_TOKEN: 'true',
  WORKSLIP_SYNTHETIC_USER_EMAIL: 'user@example.test',
  WORKSLIP_SYNTHETIC_ADMIN_EMAIL: 'admin@example.test',
};

test('local Actions runner accepts only explicit loopback Development targets', () => {
  assert.deepEqual(validateLocalActionsEnvironment(valid), {
    appUrl: 'http://127.0.0.1:5270',
    apiUrl: 'http://localhost:5262',
    userEmail: 'user@example.test',
    adminEmail: 'admin@example.test',
  });
});

test('local Actions runner rejects non-loopback app targets', () => {
  assert.throws(
    () => validateLocalActionsEnvironment({ ...valid, WORKSLIP_LOCAL_APP_URL: 'https://app.mrsoftware.dk' }),
    /loopback HTTP origin/,
  );
});

test('local Actions runner rejects non-loopback API targets', () => {
  assert.throws(
    () => validateLocalActionsEnvironment({ ...valid, WORKSLIP_LOCAL_API_URL: 'https://api.example.test' }),
    /loopback HTTP origin/,
  );
});

test('local Actions runner requires explicit dev-token opt-in', () => {
  assert.throws(
    () => validateLocalActionsEnvironment({ ...valid, WORKSLIP_ALLOW_LOCAL_DEV_TOKEN: 'false' }),
    /WORKSLIP_ALLOW_LOCAL_DEV_TOKEN must be exactly true/,
  );
});

test('local Actions runner rejects origins with path or credentials', () => {
  assert.throws(
    () => validateLocalActionsEnvironment({ ...valid, WORKSLIP_LOCAL_APP_URL: 'http://user:pass@127.0.0.1:5270/app' }),
    /loopback HTTP origin/,
  );
});
