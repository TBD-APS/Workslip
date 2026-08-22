import { runFocusedAdminScenario } from './playwright-local-admin-scenario.mjs';
import { runTimerLedgerAcceptance } from './playwright-timer-ledger-acceptance.mjs';
import { runWorksheetWave1Acceptance } from './playwright-wave1-acceptance.mjs';

for (const viewport of ['desktop', 'mobile']) {
  await runFocusedAdminScenario('worksheet-integrity', viewport);
  await runWorksheetWave1Acceptance(viewport);
}

for (const viewport of ['desktop', 'desktop-wide', 'mobile']) {
  await runTimerLedgerAcceptance(viewport);
}
