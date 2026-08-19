import { runFocusedAdminScenario } from './playwright-local-admin-scenario.mjs';
import { runCustomerWave1Acceptance } from './playwright-wave1-acceptance.mjs';

for (const viewport of ['desktop', 'mobile']) {
  await runFocusedAdminScenario('customer-lifecycle', viewport);
  await runCustomerWave1Acceptance(viewport);
}
