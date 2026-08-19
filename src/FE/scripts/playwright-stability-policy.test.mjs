import assert from 'node:assert/strict';
import test from 'node:test';
import { inspectPlaywrightSource } from './playwright-stability-policy.mjs';

test('accepts event waits that are armed before the triggering action', () => {
  const source = `
const response = page.waitForResponse(() => true);
await page.getByRole('button', { name: 'Gem' }).click();
await response;
`;
  assert.deepEqual(inspectPlaywrightSource('scenario.mjs', source), []);
});

test('accepts the shared async response helper because calling it arms the listener immediately', () => {
  const source = `
async function waitForApiResponse(page, method, pathname, statuses) { const response = await page.waitForResponse(() => true); return response; }
const response = waitForApiResponse(page, 'POST', '/api/jobs', [200]);
await page.getByRole('button', { name: 'Gem' }).click();
await response;
`;
  assert.deepEqual(inspectPlaywrightSource('scenario.mjs', source), []);
});

test('blocks passively awaited response listeners', () => {
  const findings = inspectPlaywrightSource(
    'scenario.mjs',
    `await page.waitForResponse(() => true);`,
  );
  assert.ok(findings.some((finding) => finding.rule === 'passive-response-wait'));
});

test('blocks long fixed sleeps in blocking browser scenarios', () => {
  const findings = inspectPlaywrightSource(
    'scenario.mjs',
    `await page.waitForTimeout(1_000);`,
  );
  assert.ok(findings.some((finding) => finding.rule === 'long-fixed-wait'));
});

test('allows short rendering nudges while still reporting them in suite metrics', () => {
  assert.deepEqual(
    inspectPlaywrightSource('scenario.mjs', `await page.waitForTimeout(75);`),
    [],
  );
});

test('blocks retired selectors that caused release flakes', () => {
  const source = `
page.getByRole('button', { name: 'Indstillinger og konto' });
dialog.locator('#rejection-note');
page.getByRole('dialog', { name: 'Søg i hele Workslip' });
`;
  const rules = inspectPlaywrightSource('scenario.mjs', source).map((finding) => finding.rule);
  assert.deepEqual(rules.sort(), [
    'old-account-menu-copy',
    'old-quick-nav-dialog-name',
    'old-rejection-field',
  ]);
});

test('blocks directly awaited API response helpers', () => {
  const findings = inspectPlaywrightSource(
    'scenario.mjs',
    `await waitForApiResponse(page, 'POST', '/api/jobs', [200]);`,
  );
  assert.ok(findings.some((finding) => finding.rule === 'passive-api-response-wait'));
});
