import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));

const BLOCKING_SCENARIOS = new Set([
  'playwright-ephemeral-smoke.mjs',
  'playwright-auth-brand.mjs',
  'playwright-pdf-performance.mjs',
  'playwright-critical-rare-flows.mjs',
  'playwright-critical-job-lifecycle.mjs',
  'playwright-duplicate-assignment-lifecycle.mjs',
  'playwright-copyability-lifecycle.mjs',
  'playwright-shared-state-semantics.mjs',
  'playwright-critical-domain.mjs',
  'playwright-critical-contract.mjs',
]);

const STALE_SELECTOR_RULES = [
  {
    id: 'old-account-menu-copy',
    pattern: /Indstillinger og konto/g,
    message: 'Use the stable account-menu test IDs instead of the retired UI copy.',
  },
  {
    id: 'old-rejection-field',
    pattern: /#rejection-note/g,
    message: 'The rejection field contract moved; do not resurrect the stale #rejection-note selector.',
  },
  {
    id: 'old-quick-nav-dialog-name',
    pattern: /getByRole\(\s*['"]dialog['"]\s*,\s*\{\s*name:\s*['"]Søg i hele Workslip['"]/g,
    message: 'Quick Navigator dialog is labelled by "Søg"; the longer copy belongs to the searchbox.',
  },
];

export function inspectPlaywrightSource(filename, source) {
  const findings = [];
  const lines = source.split(/\r?\n/);

  for (const rule of STALE_SELECTOR_RULES) {
    for (let index = 0; index < lines.length; index += 1) {
      rule.pattern.lastIndex = 0;
      if (rule.pattern.test(lines[index])) {
        findings.push({
          file: filename,
          line: index + 1,
          rule: rule.id,
          message: rule.message,
        });
      }
    }
  }

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];

    const fixedWait = line.match(/\.waitForTimeout\(\s*(\d[\d_]*)\s*\)/);
    if (fixedWait) {
      const milliseconds = Number(fixedWait[1].replaceAll('_', ''));
      if (milliseconds >= 500) {
        findings.push({
          file: filename,
          line: index + 1,
          rule: 'long-fixed-wait',
          message: `Replace fixed ${milliseconds}ms sleeps with a locator, URL, response, or authoritative state condition.`,
        });
      }
    }

    const definesArmedResponseHelper = /async\s+function\s+waitForApiResponse\s*\(/.test(line);
    if (!definesArmedResponseHelper && /await\s+(?:[\w.]+\.)?waitForResponse\s*\(/.test(line)) {
      findings.push({
        file: filename,
        line: index + 1,
        rule: 'passive-response-wait',
        message: 'Do not await a response listener passively. Arm the listener before the UI action, then perform the action and await the armed promise.',
      });
    }

    if (/await\s+[^;]*waitForApiResponse\s*\(/.test(line)) {
      findings.push({
        file: filename,
        line: index + 1,
        rule: 'passive-api-response-wait',
        message: 'Do not await waitForApiResponse directly. Arm it before the action that triggers the request, then await the promise afterwards.',
      });
    }
  }

  return findings;
}

/**
 * Every scenario the ephemeral runner invokes must exist on disk. A runner that
 * references a scenario script missing from the checkout aborts the whole suite
 * mid-run with a cryptic "Cannot find module" only after ~3 minutes of stack
 * setup — historically the single most common Playwright-gate failure. Detect
 * the desync here, cheaply, before any runtime setup.
 */
export function findMissingRunnerScenarios(runnerSource, availableFiles) {
  const available = new Set(availableFiles);
  const missing = [];
  const seen = new Set();
  // A trailing backslash continues a shell command onto the next physical line,
  // so `run_scenario 'label' \` followed by `scripts/foo.mjs` is one logical
  // invocation. Collapse continuations first; a per-line scan would otherwise
  // see neither token together and silently skip the scenario — reintroducing
  // the exact mid-suite "Cannot find module" abort this check guards against.
  const logicalLines = String(runnerSource ?? '')
    .replace(/\\\r?\n/g, ' ')
    .split(/\r?\n/);
  for (const line of logicalLines) {
    const match = line.match(/\brun_scenario\b[^\n]*?\bscripts\/(playwright-[a-z0-9-]+\.mjs)\b/);
    if (!match) continue;
    const file = match[1];
    if (seen.has(file)) continue;
    seen.add(file);
    if (!available.has(file)) missing.push(file);
  }
  return missing;
}

export async function inspectRunnerScenarioSync(directory = scriptDirectory) {
  const runnerPath = path.join(directory, 'run-playwright-ephemeral.sh');
  const runnerSource = await readFile(runnerPath, 'utf8');
  const entries = await readdir(directory, { withFileTypes: true });
  const availableFiles = entries
    .filter((entry) => entry.isFile())
    .map((entry) => entry.name);
  return findMissingRunnerScenarios(runnerSource, availableFiles);
}

export async function inspectBlockingPlaywrightSuite(directory = scriptDirectory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = entries
    .filter((entry) => entry.isFile() && BLOCKING_SCENARIOS.has(entry.name))
    .map((entry) => entry.name)
    .sort();

  const findings = [];
  const metrics = [];
  for (const filename of files) {
    const source = await readFile(path.join(directory, filename), 'utf8');
    findings.push(...inspectPlaywrightSource(filename, source));
    metrics.push({
      file: filename,
      responseWaits: (source.match(/waitForResponse\s*\(/g) ?? []).length,
      apiResponseWaits: (source.match(/waitForApiResponse\s*\(/g) ?? []).length,
      fixedWaits: (source.match(/\.waitForTimeout\s*\(/g) ?? []).length,
      testIds: (source.match(/getByTestId\s*\(/g) ?? []).length,
    });
  }

  return { files, findings, metrics };
}

async function main() {
  const missingScenarios = await inspectRunnerScenarioSync();
  if (missingScenarios.length > 0) {
    for (const file of missingScenarios) {
      console.error(
        `[playwright-stability] run-playwright-ephemeral.sh runs scripts/${file}, but that file is missing from the checkout.`,
      );
    }
    throw new Error(
      `Playwright runner references ${missingScenarios.length} scenario script(s) that do not exist. Add the missing script(s) or remove the run_scenario line(s).`,
    );
  }
  console.log('[playwright-stability] every runner scenario resolves to a file on disk.');

  const result = await inspectBlockingPlaywrightSuite();
  console.log(`[playwright-stability] inspected ${result.files.length} blocking Playwright modules.`);
  for (const metric of result.metrics) {
    console.log(
      `[playwright-stability] ${metric.file}: responseWaits=${metric.responseWaits}, apiResponseWaits=${metric.apiResponseWaits}, fixedWaits=${metric.fixedWaits}, testIds=${metric.testIds}`,
    );
  }

  if (result.findings.length > 0) {
    for (const finding of result.findings) {
      console.error(`[playwright-stability] ${finding.file}:${finding.line} [${finding.rule}] ${finding.message}`);
    }
    throw new Error(`Playwright stability policy found ${result.findings.length} blocking issue(s).`);
  }

  console.log('[playwright-stability] blocking suite satisfies the stability policy.');
}

const invokedPath = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : null;
if (invokedPath === import.meta.url) {
  await main();
}
