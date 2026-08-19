import { runCustomerWave1Acceptance } from './playwright-wave1-acceptance.mjs';

for (const viewport of ['desktop', 'mobile']) {
  await runCustomerWave1Acceptance(viewport);
}
