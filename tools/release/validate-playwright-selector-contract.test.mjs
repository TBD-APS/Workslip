import assert from 'node:assert/strict';
import test from 'node:test';

import { inspectAddedPlaywrightSelectors } from './validate-playwright-selector-contract.mjs';

function diffFor(line) {
  return `diff --git a/src/FE/scripts/playwright-example.mjs b/src/FE/scripts/playwright-example.mjs
--- a/src/FE/scripts/playwright-example.mjs
+++ b/src/FE/scripts/playwright-example.mjs
@@ -10,0 +11 @@
+${line}
`;
}

test('accepts a stable DOM id locator', () => {
  assert.deepEqual(
    inspectAddedPlaywrightSelectors(diffFor("await page.locator('#job-submit').click();")),
    [],
  );
});

test('accepts a dynamic stable DOM id locator', () => {
  assert.deepEqual(
    inspectAddedPlaywrightSelectors(diffFor('await page.locator(`#job-image-${imageId}`).click();')),
    [],
  );
});

test('blocks visible-copy and accessibility selector helpers', () => {
  for (const line of [
    "await page.getByText('Gem').click();",
    "await page.getByRole('button', { name: 'Gem' }).click();",
    "await page.getByPlaceholder('Kommentar').fill('x');",
    "await page.getByTestId('job-submit').click();",
  ]) {
    const findings = inspectAddedPlaywrightSelectors(diffFor(line));
    assert.ok(findings.some((finding) => finding.rule === 'stable-id-required'), line);
  }
});

test('blocks new class and element locators', () => {
  for (const line of [
    "await page.locator('.save-button').click();",
    "await page.locator('input[type=\"file\"]').setInputFiles(files);",
  ]) {
    const findings = inspectAddedPlaywrightSelectors(diffFor(line));
    assert.ok(findings.some((finding) => finding.rule === 'non-id-locator'), line);
  }
});

test('blocks visible-copy CSS plumbing', () => {
  const findings = inspectAddedPlaywrightSelectors(
    diffFor("await page.locator('[aria-label=\"Gem\"]').click();"),
  );
  assert.ok(findings.some((finding) => finding.rule === 'visible-copy-selector'));
});

test('blocks direct page selectors unless they use an id', () => {
  const blocked = inspectAddedPlaywrightSelectors(diffFor("await page.click('.save-button');"));
  assert.ok(blocked.some((finding) => finding.rule === 'non-id-direct-selector'));

  assert.deepEqual(
    inspectAddedPlaywrightSelectors(diffFor("await page.click('#job-submit');")),
    [],
  );
});

test('does not lint policy test fixtures themselves', () => {
  const diff = `diff --git a/src/FE/scripts/playwright-example.test.mjs b/src/FE/scripts/playwright-example.test.mjs
--- a/src/FE/scripts/playwright-example.test.mjs
+++ b/src/FE/scripts/playwright-example.test.mjs
@@ -1,0 +2 @@
+page.getByText('fixture');
`;
  assert.deepEqual(inspectAddedPlaywrightSelectors(diff), []);
});