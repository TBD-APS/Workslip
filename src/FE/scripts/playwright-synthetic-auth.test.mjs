import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { createContractHelpers } from './playwright-critical-contract.mjs';
import { createSyntheticAuth, INTERACTIVE_OTC_ENV } from './playwright-synthetic-auth.mjs';

const roleEmails = {
  WORKSLIP_SYNTHETIC_USER_EMAIL: 'user@example.test',
  WORKSLIP_SYNTHETIC_AUDITOR_EMAIL: 'auditor@example.test',
  WORKSLIP_SYNTHETIC_ADMIN_EMAIL: 'admin@example.test',
  WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL: 'superadmin@example.test',
};

test('public smoke does not require identities or interactive OTC', () => {
  const auth = createSyntheticAuth({
    env: { [INTERACTIVE_OTC_ENV]: 'not-relevant-to-public-smoke' },
    stdinIsTTY: false,
    stdoutIsTTY: false,
  });

  assert.doesNotThrow(() => auth.assertScenarioReady('public-smoke'));
  assert.deepEqual(auth.browserLaunchOptions('public-smoke'), { headless: true });
});

test('authenticated automation fails closed before send-code without an inbox reader', () => {
  const auth = createSyntheticAuth({ env: roleEmails, stdinIsTTY: false, stdoutIsTTY: false });

  assert.throws(
    () => auth.assertScenarioReady('auth-session'),
    (error) => error.message.includes('no approved automated inbox reader') &&
      error.message.includes('stopped before /api/auth/send-code'),
  );
});

test('the Playwright entry point fails before browser or network setup', () => {
  const script = fileURLToPath(new URL('./playwright-prod-smoke.mjs', import.meta.url));
  const result = spawnSync(process.execPath, [script], {
    encoding: 'utf8',
    timeout: 5_000,
    env: {
      ...process.env,
      ...roleEmails,
      PROD_URL: 'https://example.test',
      SCENARIO: 'auth-session',
      WORKSLIP_RELEASE_PHASE: 'live',
      WORKSLIP_TEST_TARGET: 'staging',
      WORKSLIP_ALLOW_DESTRUCTIVE_PLAYWRIGHT: 'true',
      [INTERACTIVE_OTC_ENV]: '',
    },
  });
  const output = `${result.stdout}\n${result.stderr}`;

  assert.notEqual(result.status, 0);
  assert.match(output, /stopped before \/api\/auth\/send-code/);
  assert.doesNotMatch(output, /@example\.test/);
});

test('the underlying orchestrator cannot bypass the production policy', () => {
  const script = fileURLToPath(new URL('./playwright-prod-smoke.mjs', import.meta.url));
  const result = spawnSync(process.execPath, [script], {
    encoding: 'utf8',
    timeout: 5_000,
    env: {
      ...process.env,
      PROD_URL: 'https://example.test',
      SCENARIO: 'assignment-lifecycle',
      WORKSLIP_RELEASE_PHASE: 'prelive',
      WORKSLIP_TEST_TARGET: 'production',
      WORKSLIP_ALLOW_DESTRUCTIVE_PLAYWRIGHT: 'true',
    },
  });
  const output = `${result.stdout}\n${result.stderr}`;

  assert.notEqual(result.status, 0);
  assert.match(output, /Production permits only the write-free public-smoke scenario/);
});

test('interactive OTC requires both an explicit opt-in and a TTY', () => {
  const auth = createSyntheticAuth({
    env: { ...roleEmails, [INTERACTIVE_OTC_ENV]: 'true' },
    stdinIsTTY: false,
    stdoutIsTTY: true,
  });

  assert.throws(
    () => auth.assertScenarioReady('auth-session'),
    (error) => error.message.includes('requires an interactive TTY') &&
      error.message.includes('stopped before /api/auth/send-code'),
  );
});

test('interactive OTC uses a headed browser and role variables', () => {
  const auth = createSyntheticAuth({
    env: { ...roleEmails, [INTERACTIVE_OTC_ENV]: 'true' },
    stdinIsTTY: true,
    stdoutIsTTY: true,
  });

  assert.doesNotThrow(() => auth.assertScenarioReady('auth-session'));
  assert.deepEqual(auth.browserLaunchOptions('auth-session'), { headless: false });
  assert.equal(auth.emailForRole('Admin'), roleEmails.WORKSLIP_SYNTHETIC_ADMIN_EMAIL);
});

test('missing role identity names the variable without logging another address', () => {
  const env = { ...roleEmails };
  delete env.WORKSLIP_SYNTHETIC_AUDITOR_EMAIL;
  const auth = createSyntheticAuth({ env, stdinIsTTY: false, stdoutIsTTY: false });

  assert.throws(
    () => auth.assertScenarioReady('auth-session'),
    (error) => error.message === 'WORKSLIP_SYNTHETIC_AUDITOR_EMAIL is required for authenticated Playwright scenarios.',
  );
});

test('ambiguous interactive opt-in values are rejected', () => {
  assert.throws(
    () => createSyntheticAuth({ env: { ...roleEmails, [INTERACTIVE_OTC_ENV]: ' true ' } })
      .assertScenarioReady('auth-session'),
    new RegExp(`${INTERACTIVE_OTC_ENV} must be exactly true`),
  );
});

test('artifact URL redaction removes OTC path values', () => {
  const { redact, safeUrl } = createContractHelpers({ API_TIMEOUT: 1, UI_TIMEOUT: 1, postman: {} });
  const sensitiveUrl = 'https://api.example.test/api/auth/verify-code/123456?state=secret';

  assert.doesNotMatch(redact(sensitiveUrl), /123456|secret/);
  assert.doesNotMatch(safeUrl(sensitiveUrl), /123456|secret/);
  assert.match(safeUrl(sensitiveUrl), /verify-code\/REDACTED/);
});

test('artifact redaction removes email addresses from messages and URLs', () => {
  const { redact, safeUrl } = createContractHelpers({ API_TIMEOUT: 1, UI_TIMEOUT: 1, postman: {} });
  const email = 'person@example.test';

  assert.doesNotMatch(redact(`Failed for ${email}`), /person@example\.test/);
  assert.doesNotMatch(safeUrl(`https://api.example.test/invites/${email}?email=${email}`), /person@example\.test/);
});
