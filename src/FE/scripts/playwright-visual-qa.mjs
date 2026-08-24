import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { requireLoopbackOrigin } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270',
  'WORKSLIP_PLAYWRIGHT_APP_URL',
);
const UI_TIMEOUT = 25_000;
const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(SCRIPT_DIR, '../../..');
const VISUAL_QA_PROJECT = path.join(REPO_ROOT, 'tools/visual-qa/Workslip.VisualQa.csproj');
const EVIDENCE_DIR = process.env.WORKSLIP_VISUAL_QA_EVIDENCE_DIR
  || path.join(process.env.RUNNER_TEMP || os.tmpdir(), 'workslip-visual-qa');

const { chromium } = await import('playwright');
const browser = await chromium.launch({ headless: true });
const cases = [
  { name: 'desktop-1280', viewport: { width: 1280, height: 800 } },
  { name: 'mobile-390', viewport: { width: 390, height: 844 } },
];

await mkdir(EVIDENCE_DIR, { recursive: true });

try {
  for (const testCase of cases) {
    await verifyVisualPresence(testCase);
  }
  console.log(`[visual-qa] Clippy visual presence passed on desktop + mobile. Evidence: ${EVIDENCE_DIR}`);
} finally {
  await browser.close();
}

async function verifyVisualPresence({ name, viewport }) {
  const context = await browser.newContext({ viewport, locale: 'da-DK', timezoneId: 'Europe/Copenhagen' });
  try {
    const page = await context.newPage();
    const navigation = await page.goto(`${APP_URL}/login`, { waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    assert.ok(navigation?.ok(), `${name}: /login returned HTTP ${navigation?.status() ?? 'unknown'}.`);
    await page.locator('#login-card').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    // WOR-771 contract: Clippy is visible by default, while its bubble stays closed.
    await page.evaluate(() => localStorage.removeItem('workslip.flag.help-wizard'));
    await page.reload({ waitUntil: 'domcontentloaded', timeout: UI_TIMEOUT });
    await page.locator('#login-card').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const wizard = page.locator('#help-wizard');
    const toggle = page.locator('#help-wizard-toggle');
    await toggle.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false', `${name}: launcher must start closed.`);

    const bounds = await wizard.boundingBox();
    assert.ok(bounds, `${name}: HelpWizard must expose DOM bounds.`);

    const visiblePath = path.join(EVIDENCE_DIR, `${name}-clippy-visible.png`);
    const hiddenPath = path.join(EVIDENCE_DIR, `${name}-clippy-hidden-control.png`);
    const metadataPath = path.join(EVIDENCE_DIR, `${name}-clippy.json`);
    const resultPath = path.join(EVIDENCE_DIR, `${name}-clippy-result.json`);

    await page.screenshot({ path: visiblePath });
    await wizard.evaluate((element) => {
      element.dataset.visualQaOriginalOpacity = element.style.opacity;
      element.style.setProperty('opacity', '0', 'important');
    });
    await page.screenshot({ path: hiddenPath });
    await wizard.evaluate((element) => {
      const previous = element.dataset.visualQaOriginalOpacity || '';
      element.style.opacity = previous;
      delete element.dataset.visualQaOriginalOpacity;
    });

    await writeFile(metadataPath, JSON.stringify({
      name: `help-wizard:${name}`,
      bounds,
      viewport,
      minMeanDelta: 2,
      minChangedPixelRatio: 0.015,
      pixelDeltaThreshold: 8,
    }, null, 2));

    const positive = runAnalyzer(visiblePath, hiddenPath, metadataPath, resultPath);
    assert.equal(positive.status, 0, `${name}: visual analyzer rejected visible Clippy. ${positive.stderr || positive.stdout}`);

    // Proof that DOM existence alone is not enough: identical screenshots simulate an
    // invisible/transparent element and must be blocked by the visual layer.
    if (name === 'desktop-1280') {
      const negativeResult = path.join(EVIDENCE_DIR, `${name}-intentional-invisible-result.json`);
      const negative = runAnalyzer(visiblePath, visiblePath, metadataPath, negativeResult);
      assert.equal(negative.status, 2, `visual analyzer must block a DOM-present but visually absent fixture. ${negative.stderr || negative.stdout}`);
    }
  } finally {
    await context.close();
  }
}

function runAnalyzer(visiblePath, hiddenPath, metadataPath, resultPath) {
  return spawnSync('dotnet', [
    'run',
    '--project', VISUAL_QA_PROJECT,
    '--configuration', 'Release',
    '--',
    visiblePath,
    hiddenPath,
    metadataPath,
    resultPath,
  ], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
    env: process.env,
  });
}
