import { runFocusedAdminScenario } from './playwright-local-admin-scenario.mjs';

for (const viewport of ['desktop', 'mobile']) {
  await runFocusedAdminScenario('worksheet-integrity', viewport);
}
