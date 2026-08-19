import { runLocalAdminScenario } from './playwright-local-assignment.mjs';

for (const viewport of ['desktop', 'mobile']) {
  await runLocalAdminScenario('worksheet-integrity', viewport);
}
