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
const MASCOT_PAINT_POLL_MS = 80;
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

    // The mascot is rendered by a CSS background image. A DOM-visible SVG can have
    // valid bounds — and Chromium can even resolve background-image decode() — before
    // the element's own layer has actually painted, which makes the visible/hidden
    // screenshots identical and produces a false "visually absent" result. Worse,
    // Chromium resolves decode() as successful on a truncated/corrupt PNG, so an
    // asset check cannot prove on-screen presence. Poll the live launcher region
    // against its hidden control until real pixels are painted before capturing.
    const bounds = await waitForMascotToPaint(page, wizard, name);

    const visiblePath = path.join(EVIDENCE_DIR, `${name}-clippy-visible.png`);
    const hiddenPath = path.join(EVIDENCE_DIR, `${name}-clippy-hidden-control.png`);
    const metadataPath = path.join(EVIDENCE_DIR, `${name}-clippy.json`);
    const resultPath = path.join(EVIDENCE_DIR, `${name}-clippy-result.json`);

    await page.screenshot({ path: visiblePath });
    await wizard.evaluate((element) => {
      element.dataset.visualQaOriginalOpacity = element.style.opacity;
      element.style.setProperty('opacity', '0', 'important');
    });
    await waitForPaintFrames(page);
    await page.screenshot({ path: hiddenPath });
    await wizard.evaluate((element) => {
      const previous = element.dataset.visualQaOriginalOpacity || '';
      element.style.opacity = previous;
      delete element.dataset.visualQaOriginalOpacity;
    });
    await waitForPaintFrames(page);

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

// Prove the mascot is actually painted on screen (not merely present in the DOM
// with a "decoded" asset) and return its DOM bounds. Returns only once the live
// launcher region diverges from its hidden control, so the caller's evidence
// capture is guaranteed to contain painted pixels.
async function waitForMascotToPaint(page, wizard, name) {
  await assertMascotAssetDecodable(page, name);

  const bounds = await wizard.boundingBox();
  assert.ok(bounds, `${name}: HelpWizard must expose DOM bounds.`);
  const clip = {
    x: Math.max(0, Math.round(bounds.x)),
    y: Math.max(0, Math.round(bounds.y)),
    width: Math.max(1, Math.round(bounds.width)),
    height: Math.max(1, Math.round(bounds.height)),
  };

  // Blank control: with the mascot hidden the region is a flat colour that
  // encodes to a tiny PNG. A painted mascot is many times larger, which lets us
  // detect genuine paint without a pixel decoder while tolerating the mascot's
  // idle micro-animations (which byte-equality checks would trip over).
  const blankShot = await withHiddenWizard(page, wizard, () => page.screenshot({ clip }));
  const paintedThreshold = Math.max(blankShot.length * 2, blankShot.length + 512);

  const deadline = Date.now() + UI_TIMEOUT;
  let consecutivePainted = 0;
  while (Date.now() < deadline) {
    await waitForPaintFrames(page);
    const shot = await page.screenshot({ clip });
    if (shot.length >= paintedThreshold) {
      consecutivePainted += 1;
      if (consecutivePainted >= 2) return bounds;
    } else {
      consecutivePainted = 0;
    }
    await page.waitForTimeout(MASCOT_PAINT_POLL_MS);
  }

  return assert.fail(
    `${name}: Clippy mascot never painted pixels within ${UI_TIMEOUT}ms; the launcher region stayed blank `
    + '(asset missing/corrupt, or the element rendered transparently).',
  );
}

async function assertMascotAssetDecodable(page, name) {
  const decoded = await page.evaluate(async () => {
    const mascot = document.getElementById('help-wizard-character');
    if (!mascot) return { ok: false, reason: 'missing #help-wizard-character' };

    const backgroundImage = getComputedStyle(mascot).backgroundImage;
    const match = backgroundImage.match(/^url\(["']?(.*?)["']?\)$/);
    if (!match?.[1]) return { ok: false, reason: `no background-image (${backgroundImage})` };

    const image = new Image();
    image.src = new URL(match[1], document.baseURI).href;
    try {
      await image.decode();
    } catch (error) {
      return { ok: false, reason: `background-image failed to decode: ${String(error)}` };
    }
    return {
      ok: image.naturalWidth > 0 && image.naturalHeight > 0,
      reason: 'background-image has zero intrinsic size',
    };
  });
  assert.ok(decoded.ok, `${name}: mascot asset is not a usable image (${decoded.reason}).`);
}

async function withHiddenWizard(page, wizard, action) {
  await wizard.evaluate((element) => {
    element.dataset.visualQaGateOpacity = element.style.opacity;
    element.style.setProperty('opacity', '0', 'important');
  });
  await waitForPaintFrames(page);
  try {
    return await action();
  } finally {
    await wizard.evaluate((element) => {
      element.style.opacity = element.dataset.visualQaGateOpacity || '';
      delete element.dataset.visualQaGateOpacity;
    });
    await waitForPaintFrames(page);
  }
}

async function waitForPaintFrames(page) {
  await page.evaluate(() => new Promise((resolve) => {
    requestAnimationFrame(() => requestAnimationFrame(resolve));
  }));
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
