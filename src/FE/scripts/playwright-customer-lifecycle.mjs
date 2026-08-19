import { runLocalAdminScenario } from './playwright-local-assignment.mjs';

for (const viewport of ['desktop', 'mobile']) {
  await runLocalAdminScenario('customer-lifecycle', viewport);
}
