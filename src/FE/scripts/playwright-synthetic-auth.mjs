import process from 'node:process';

export const INTERACTIVE_OTC_ENV = 'WORKSLIP_PLAYWRIGHT_INTERACTIVE_OTC';

const ROLE_ENV_NAMES = Object.freeze({
  User: 'WORKSLIP_SYNTHETIC_USER_EMAIL',
  Auditor: 'WORKSLIP_SYNTHETIC_AUDITOR_EMAIL',
  Admin: 'WORKSLIP_SYNTHETIC_ADMIN_EMAIL',
  Superadmin: 'WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL',
});

export function createSyntheticAuth({
  env = process.env,
  stdinIsTTY = process.stdin.isTTY === true,
  stdoutIsTTY = process.stdout.isTTY === true,
} = {}) {
  const interactiveSetting = env[INTERACTIVE_OTC_ENV];
  const interactiveRequested = interactiveSetting === 'true';

  return {
    assertScenarioReady(scenario) {
      if (scenario === 'public-smoke') return;

      if (interactiveSetting && interactiveSetting !== 'true') {
        throw new Error(`${INTERACTIVE_OTC_ENV} must be exactly true when interactive OTC is deliberately enabled.`);
      }

      for (const envName of Object.values(ROLE_ENV_NAMES)) requireEnv(env, envName);

      if (!interactiveRequested) {
        throw new Error(
          `Authenticated Playwright scenario ${scenario} is unavailable because no approved automated inbox reader is configured. ` +
          `The run stopped before /api/auth/send-code. For a local headed run with a TTY, set ${INTERACTIVE_OTC_ENV}=true.`,
        );
      }
      if (!stdinIsTTY || !stdoutIsTTY) {
        throw new Error(
          `Authenticated Playwright scenario ${scenario} requires an interactive TTY when ${INTERACTIVE_OTC_ENV}=true. ` +
          'The run stopped before /api/auth/send-code.',
        );
      }
    },

    browserLaunchOptions(scenario) {
      return { headless: scenario === 'public-smoke' || !interactiveRequested };
    },

    emailForRole(role) {
      const envName = ROLE_ENV_NAMES[role];
      if (!envName) throw new Error(`Unsupported synthetic role: ${role}`);
      return requireEnv(env, envName).toLowerCase();
    },
  };
}

function requireEnv(env, name) {
  const value = env[name]?.trim();
  if (!value) throw new Error(`${name} is required for authenticated Playwright scenarios.`);
  return value;
}
