import { appendFile, readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptPath = fileURLToPath(import.meta.url);
const repositoryRoot = path.resolve(path.dirname(scriptPath), '../..');
const defaultConfigPath = path.join(repositoryRoot, 'config/release-environments.json');
const allowedPhases = new Set(['prelive', 'live']);
const allowedEnvironments = new Set(['production', 'staging']);

function requireRecord(value, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${label} must be an object.`);
  }
  return value;
}

function requireBoolean(value, label) {
  if (typeof value !== 'boolean') throw new Error(`${label} must be a boolean.`);
  return value;
}

function validateOrigin(value, label, required) {
  if (value === null && !required) return null;
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error(`${label} must be a non-empty HTTPS origin.`);
  }

  let parsed;
  try {
    parsed = new URL(value);
  } catch {
    throw new Error(`${label} is not a valid URL.`);
  }

  if (
    parsed.protocol !== 'https:'
    || parsed.username
    || parsed.password
    || parsed.search
    || parsed.hash
    || (parsed.pathname !== '/' && parsed.pathname !== '')
  ) {
    throw new Error(`${label} must be an HTTPS origin without credentials, path, query, or fragment.`);
  }

  return parsed.origin;
}

function validateEnvironment(value, name, requireUrl) {
  const environment = requireRecord(value, `environments.${name}`);
  const url = validateOrigin(environment.url, `environments.${name}.url`, requireUrl);
  const enableDevelopmentEndpoints = requireBoolean(
    environment.enableDevelopmentEndpoints,
    `environments.${name}.enableDevelopmentEndpoints`,
  );
  const allowDestructivePlaywright = requireBoolean(
    environment.allowDestructivePlaywright,
    `environments.${name}.allowDestructivePlaywright`,
  );

  if (allowDestructivePlaywright && !enableDevelopmentEndpoints) {
    throw new Error(
      `environments.${name} cannot allow destructive Playwright while development endpoints are disabled.`,
    );
  }

  return { url, enableDevelopmentEndpoints, allowDestructivePlaywright };
}

export function validateReleaseConfig(value) {
  const root = requireRecord(value, 'release configuration');
  if (!allowedPhases.has(root.phase)) {
    throw new Error('phase must be either prelive or live.');
  }

  const environments = requireRecord(root.environments, 'environments');
  const production = validateEnvironment(environments.production, 'production', true);
  const staging = validateEnvironment(environments.staging, 'staging', root.phase === 'live');

  if (root.phase === 'prelive') {
    if (!production.enableDevelopmentEndpoints || !production.allowDestructivePlaywright) {
      throw new Error('Pre-live production must enable release-test endpoints and destructive Playwright.');
    }
    if (staging.url !== null || staging.enableDevelopmentEndpoints || staging.allowDestructivePlaywright) {
      throw new Error('Staging must remain disabled until the live two-environment phase.');
    }
  } else {
    if (production.enableDevelopmentEndpoints || production.allowDestructivePlaywright) {
      throw new Error('Live production must disable development endpoints and destructive Playwright.');
    }
    if (!staging.enableDevelopmentEndpoints || !staging.allowDestructivePlaywright) {
      throw new Error('Live staging must enable release-test endpoints and destructive Playwright.');
    }
  }

  return {
    phase: root.phase,
    environments: { production, staging },
  };
}

export async function loadReleaseConfig(configPath = defaultConfigPath) {
  const content = await readFile(configPath, 'utf8');
  return validateReleaseConfig(JSON.parse(content));
}

export function resolveReleaseEnvironment(config, environmentName, { requireRunnable = false } = {}) {
  if (!allowedEnvironments.has(environmentName)) {
    throw new Error('environment must be production or staging.');
  }

  const environment = config.environments[environmentName];
  if (requireRunnable && !environment.url) {
    throw new Error(`${environmentName} is not configured as a runnable release-test target.`);
  }

  return {
    phase: config.phase,
    environment: environmentName,
    url: environment.url,
    enableDevelopmentEndpoints: environment.enableDevelopmentEndpoints,
    allowDestructivePlaywright: environment.allowDestructivePlaywright,
  };
}

function parseArguments(args) {
  const result = {
    environment: null,
    configPath: defaultConfigPath,
    githubOutput: false,
    requireRunnable: false,
  };

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    if (argument === '--environment') result.environment = args[++index];
    else if (argument === '--config') result.configPath = path.resolve(args[++index]);
    else if (argument === '--github-output') result.githubOutput = true;
    else if (argument === '--require-runnable') result.requireRunnable = true;
    else throw new Error(`Unknown argument: ${argument}`);
  }

  if (!result.environment) throw new Error('--environment is required.');
  return result;
}

async function writeGithubOutput(result) {
  const outputPath = process.env.GITHUB_OUTPUT;
  if (!outputPath) throw new Error('GITHUB_OUTPUT is required with --github-output.');

  const lines = [
    `phase=${result.phase}`,
    `environment=${result.environment}`,
    `url=${result.url ?? ''}`,
    `enable_development_endpoints=${result.enableDevelopmentEndpoints}`,
    `allow_destructive_playwright=${result.allowDestructivePlaywright}`,
  ];
  await appendFile(outputPath, `${lines.join('\n')}\n`, 'utf8');
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const config = await loadReleaseConfig(options.configPath);
  const result = resolveReleaseEnvironment(config, options.environment, {
    requireRunnable: options.requireRunnable,
  });

  if (options.githubOutput) await writeGithubOutput(result);
  else process.stdout.write(`${JSON.stringify(result)}\n`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === scriptPath) {
  await main();
}
