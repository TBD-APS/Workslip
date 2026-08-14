import process from 'node:process';

const supportedScenarios = new Set([
  'public-smoke',
  'auth-session',
  'kls-lifecycle',
  'rejection-loop',
  'draft-recovery',
  'role-tenant-isolation',
  'invitation-onboarding',
  'assignment-lifecycle',
  'customer-lifecycle',
  'worksheet-integrity',
  'diverse-lifecycle',
  'all-critical',
]);

export function validateReleaseRunEnvironment(env = process.env) {
  const scenario = env.SCENARIO ?? 'public-smoke';
  const phase = env.WORKSLIP_RELEASE_PHASE ?? '';
  const target = env.WORKSLIP_TEST_TARGET ?? '';
  const allowDestructive = env.WORKSLIP_ALLOW_DESTRUCTIVE_PLAYWRIGHT === 'true';
  const appUrl = validateReleaseOrigin(env.PROD_URL ?? '');
  const isPublicSmoke = scenario === 'public-smoke';

  if (!supportedScenarios.has(scenario)) {
    throw new Error(`Unsupported Playwright scenario: ${scenario}.`);
  }
  if (phase !== 'prelive' && phase !== 'live') {
    throw new Error('WORKSLIP_RELEASE_PHASE must be prelive or live.');
  }
  if (target !== 'production' && target !== 'staging') {
    throw new Error('WORKSLIP_TEST_TARGET must be production or staging.');
  }
  if (target === 'staging' && phase !== 'live') {
    throw new Error('Staging release testing is not configured before the isolated live-phase target exists.');
  }
  if (target === 'production' && !isPublicSmoke) {
    throw new Error('Production permits only the write-free public-smoke scenario.');
  }
  if (!isPublicSmoke && !allowDestructive) {
    throw new Error(
      `Scenario ${scenario} requires an authenticated isolated release-test environment and is blocked for ${target}.`,
    );
  }

  return { scenario, phase, target, allowDestructive, appUrl, isPublicSmoke };
}

function validateReleaseOrigin(value) {
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error('PROD_URL must be a configured HTTPS origin without credentials, path, query, or fragment.');
  }

  if (
    url.protocol !== 'https:'
    || url.username
    || url.password
    || url.search
    || url.hash
    || (url.pathname !== '/' && url.pathname !== '')
  ) {
    throw new Error('PROD_URL must be a configured HTTPS origin without credentials, path, query, or fragment.');
  }

  return url.origin;
}
