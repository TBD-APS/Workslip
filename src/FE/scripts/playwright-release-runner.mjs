import process from 'node:process';

const scenario = process.env.SCENARIO ?? 'public-smoke';
const phase = process.env.WORKSLIP_RELEASE_PHASE ?? '';
const target = process.env.WORKSLIP_TEST_TARGET ?? '';
const allowDestructive = process.env.WORKSLIP_ALLOW_DESTRUCTIVE_PLAYWRIGHT === 'true';
const appUrl = process.env.PROD_URL ?? '';
const isWriteFree = scenario === 'public-smoke';

if (phase !== 'prelive' && phase !== 'live') {
  throw new Error('WORKSLIP_RELEASE_PHASE must be prelive or live.');
}
if (target !== 'production' && target !== 'staging') {
  throw new Error('WORKSLIP_TEST_TARGET must be production or staging.');
}
if (!appUrl.startsWith('https://')) {
  throw new Error('PROD_URL must be a configured HTTPS release-test target.');
}
if (!isWriteFree && !allowDestructive) {
  throw new Error(
    `Scenario ${scenario} can write data and is blocked for the configured ${target} environment.`,
  );
}
if (target === 'production' && phase === 'live' && !isWriteFree) {
  throw new Error('Live production permits only the write-free public-smoke scenario.');
}
if (target === 'staging' && phase !== 'live') {
  throw new Error('Staging release testing is enabled only after the two-environment live transition.');
}

if (scenario === 'notification-navigation') {
  await import('./playwright-notification-navigation.mjs');
} else {
  await import('./playwright-prod-smoke.mjs');
}
