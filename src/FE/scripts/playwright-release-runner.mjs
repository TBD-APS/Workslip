import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { validateReleaseRunEnvironment } from './playwright-release-policy.mjs';

const scriptPath = fileURLToPath(import.meta.url);

export { validateReleaseRunEnvironment } from './playwright-release-policy.mjs';

async function main() {
  validateReleaseRunEnvironment();
  await import('./playwright-prod-smoke.mjs');
}

if (process.argv[1] && path.resolve(process.argv[1]) === scriptPath) {
  await main();
}
