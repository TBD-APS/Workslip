#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { appendFileSync, readFileSync } from 'node:fs';
import process from 'node:process';
import { pathToFileURL } from 'node:url';

const UI_RUNTIME_PREFIXES = [
  'src/FE/src/features/',
  'src/FE/src/components/',
  'src/FE/src/pages/',
];

const UI_RUNTIME_EXTENSIONS = new Set(['.tsx', '.ts', '.css', '.scss']);
const PLAYWRIGHT_SCRIPT_PATTERN = /^playwright-[a-z0-9-]+\.mjs$/;
const PLAYWRIGHT_RUNNER_PATH = 'src/FE/scripts/run-playwright-ephemeral.sh';

const FLOW_RULES = [
  { flow: 'auth-session', patterns: [/\/auth\//i, /Login/i, /Otp/i, /session/i] },
  { flow: 'job-wizard', patterns: [/features\/jobs\//i, /JobWizard/i, /JobCreate/i, /JobDetails/i] },
  { flow: 'overview-navigation', patterns: [/Overview/i, /overblik/i] },
  { flow: 'worksheet', patterns: [/worksheet/i, /timesheet/i, /Timer/i] },
  { flow: 'notifications', patterns: [/notification/i, /ActivityFeed/i, /Conversation/i, /Inbox/i] },
  { flow: 'customer-lifecycle', patterns: [/features\/customers\//i, /Customer/i] },
  { flow: 'people-lifecycle', patterns: [/features\/(users|people)\//i, /User/i, /People/i] },
  { flow: 'documents', patterns: [/features\/docs\//i, /Document/i, /Upload/i] },
];

function extensionOf(path) {
  const match = path.match(/(\.[^.\/]+)$/);
  return match?.[1]?.toLowerCase() ?? '';
}

export function isUiRuntimePath(path) {
  if (!UI_RUNTIME_PREFIXES.some((prefix) => path.startsWith(prefix))) return false;
  if (!UI_RUNTIME_EXTENSIONS.has(extensionOf(path))) return false;
  if (/\.(test|spec)\.[^.]+$/i.test(path)) return false;
  if (/\/tests?\//i.test(path) || /\/__tests__\//i.test(path)) return false;
  if (/generated|api\/model|api\/client/i.test(path)) return false;
  return true;
}

export function inferBrowserFlows(paths) {
  const flows = new Set();
  for (const path of paths) {
    for (const rule of FLOW_RULES) {
      if (rule.patterns.some((pattern) => pattern.test(path))) flows.add(rule.flow);
    }
  }
  if (paths.length > 0 && flows.size === 0) flows.add('shared-ui');
  return [...flows].sort();
}

function splitCsv(value) {
  return String(value ?? '')
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
}

export function parseBrowserScriptMappings(value) {
  return splitCsv(value).map((entry) => {
    const separator = entry.indexOf('=');
    if (separator <= 0 || separator === entry.length - 1) {
      return { raw: entry, flow: '', script: '' };
    }
    return {
      raw: entry,
      flow: entry.slice(0, separator).trim().toLowerCase(),
      script: entry.slice(separator + 1).trim(),
    };
  });
}

export function parseEvidence(body) {
  const fields = new Map();
  for (const line of String(body ?? '').split(/\r?\n/)) {
    const match = line.match(/^\s*Browser-([A-Za-z-]+):\s*(.*?)\s*$/i);
    if (!match) continue;
    fields.set(match[1].toLowerCase(), match[2].trim());
  }

  return {
    evidence: (fields.get('evidence') ?? '').toLowerCase(),
    scenarios: splitCsv(fields.get('scenarios')),
    scriptMappings: parseBrowserScriptMappings(fields.get('scripts')),
    viewports: splitCsv(fields.get('viewports')),
    // Legacy mutable fields remain parseable so old PR bodies stay harmlessly readable,
    // but they are not merge-gating state. Exact-head CI owns runtime truth.
    result: (fields.get('result') ?? '').toLowerCase(),
    pageErrors: fields.get('page-errors') ?? '',
    consoleErrors: fields.get('console-errors') ?? '',
  };
}

export function registeredBrowserScripts(runnerSource) {
  const scripts = new Set();
  for (const line of String(runnerSource ?? '').split(/\r?\n/)) {
    const match = line.match(/\brun_scenario\b.*?\s+["']?scripts\/(playwright-[a-z0-9-]+\.mjs)["']?(?:\s|$)/i);
    if (match) scripts.add(match[1]);
  }
  return scripts;
}

export function validateBrowserEvidence({ changedPaths, body, runnerSource = '' }) {
  const uiPaths = changedPaths.filter(isUiRuntimePath);
  const requiredFlows = inferBrowserFlows(uiPaths);
  const required = uiPaths.length > 0;
  const evidence = parseEvidence(body);
  const errors = [];

  if (!required) {
    return { required, requiredFlows, uiPaths, evidence, errors };
  }

  if (evidence.evidence !== 'required') {
    errors.push('UI runtime changes require Browser-Evidence: required. There is no exemption path.');
    return { required, requiredFlows, uiPaths, evidence, errors };
  }

  const scenarioSet = new Set(evidence.scenarios.map((scenario) => scenario.toLowerCase()));
  for (const flow of requiredFlows) {
    if (!scenarioSet.has(flow.toLowerCase())) {
      errors.push(`Missing Browser-Scenarios entry for inferred flow: ${flow}.`);
    }
  }

  if (evidence.viewports.length === 0) {
    errors.push('Required browser evidence must declare Browser-Viewports.');
  }

  if (evidence.scriptMappings.length === 0) {
    errors.push('Required browser evidence must declare Browser-Scripts as flow=playwright-script.mjs mappings.');
  }

  const registeredScripts = registeredBrowserScripts(runnerSource);
  const mappingsByFlow = new Map();
  for (const mapping of evidence.scriptMappings) {
    if (!mapping.flow || !mapping.script) {
      errors.push(`Invalid Browser-Scripts entry '${mapping.raw}'. Use flow=playwright-script.mjs.`);
      continue;
    }
    if (!PLAYWRIGHT_SCRIPT_PATTERN.test(mapping.script)) {
      errors.push(`Invalid Playwright script name '${mapping.script}' for flow '${mapping.flow}'.`);
      continue;
    }
    if (!registeredScripts.has(mapping.script)) {
      errors.push(`Browser-Scripts references '${mapping.script}' for '${mapping.flow}', but that script is not registered in ${PLAYWRIGHT_RUNNER_PATH}.`);
    }
    const flowScripts = mappingsByFlow.get(mapping.flow) ?? [];
    flowScripts.push(mapping.script);
    mappingsByFlow.set(mapping.flow, flowScripts);
  }

  for (const flow of requiredFlows) {
    if (!mappingsByFlow.has(flow.toLowerCase())) {
      errors.push(`Missing Browser-Scripts mapping for inferred flow: ${flow}.`);
    }
  }

  return { required, requiredFlows, uiPaths, evidence, errors };
}

function gitChangedPaths(base, head) {
  const output = execFileSync('git', ['diff', '--name-only', `${base}...${head}`], { encoding: 'utf8' });
  return output.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
}

function parseArgs(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === '--base') options.base = argv[++index];
    else if (arg === '--head') options.head = argv[++index];
    else if (arg === '--body-file') options.bodyFile = argv[++index];
    else throw new Error(`Unknown argument: ${arg}`);
  }
  if (!options.base || !options.head || !options.bodyFile) {
    throw new Error('--base, --head and --body-file are required.');
  }
  return options;
}

function report(result) {
  console.log(`Browser evidence required: ${result.required}`);
  console.log(`UI runtime files: ${result.uiPaths.length}`);
  console.log(`Inferred browser flows: ${result.requiredFlows.join(', ') || 'none'}`);

  if (result.uiPaths.length > 0) {
    console.log('\nUI runtime paths:');
    for (const path of result.uiPaths) console.log(`- ${path}`);
  }

  if (result.errors.length > 0) {
    console.error('\nBROWSER_EVIDENCE_BLOCKED');
    for (const error of result.errors) console.error(`- ${error}`);
  } else if (result.required) {
    console.log('\nBrowser intent accepted. Declared flows are bound to registered exact-head Playwright scripts; runtime pass/failure is owned by CI Gate.');
  } else {
    console.log('\nNo browser evidence declaration required for this diff.');
  }
}

function main() {
  const { base, head, bodyFile } = parseArgs(process.argv.slice(2));
  const changedPaths = gitChangedPaths(base, head);
  const body = readFileSync(bodyFile, 'utf8');
  const runnerSource = readFileSync(PLAYWRIGHT_RUNNER_PATH, 'utf8');
  const result = validateBrowserEvidence({ changedPaths, body, runnerSource });

  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, `browser_required=${result.required}\n`);
    appendFileSync(process.env.GITHUB_OUTPUT, `required_flows=${result.requiredFlows.join(',')}\n`);
  }

  report(result);
  if (result.errors.length > 0) process.exitCode = 43;
}

const isEntryPoint = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isEntryPoint) {
  try {
    main();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}