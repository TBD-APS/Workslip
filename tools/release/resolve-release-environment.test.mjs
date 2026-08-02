import assert from 'node:assert/strict';
import test from 'node:test';
import {
  resolveReleaseEnvironment,
  validateReleaseConfig,
} from './resolve-release-environment.mjs';

const preliveConfig = {
  phase: 'prelive',
  environments: {
    production: {
      url: 'https://app.mrsoftware.dk',
      enableDevelopmentEndpoints: true,
      allowDestructivePlaywright: true,
    },
    staging: {
      url: null,
      enableDevelopmentEndpoints: false,
      allowDestructivePlaywright: false,
    },
  },
};

const liveConfig = {
  phase: 'live',
  environments: {
    production: {
      url: 'https://app.mrsoftware.dk',
      enableDevelopmentEndpoints: false,
      allowDestructivePlaywright: false,
    },
    staging: {
      url: 'https://staging.app.mrsoftware.dk',
      enableDevelopmentEndpoints: true,
      allowDestructivePlaywright: true,
    },
  },
};

test('pre-live production explicitly allows the current full release-test suite', () => {
  const config = validateReleaseConfig(preliveConfig);
  assert.deepEqual(resolveReleaseEnvironment(config, 'production'), {
    phase: 'prelive',
    environment: 'production',
    url: 'https://app.mrsoftware.dk',
    enableDevelopmentEndpoints: true,
    allowDestructivePlaywright: true,
  });
});

test('live production is fail-closed while staging carries full release testing', () => {
  const config = validateReleaseConfig(liveConfig);
  assert.equal(config.environments.production.enableDevelopmentEndpoints, false);
  assert.equal(config.environments.production.allowDestructivePlaywright, false);
  assert.equal(config.environments.staging.enableDevelopmentEndpoints, true);
  assert.equal(config.environments.staging.allowDestructivePlaywright, true);
});

test('live phase without a runnable staging environment is rejected', () => {
  assert.throws(
    () => validateReleaseConfig({
      ...liveConfig,
      environments: {
        ...liveConfig.environments,
        staging: {
          url: null,
          enableDevelopmentEndpoints: false,
          allowDestructivePlaywright: false,
        },
      },
    }),
    /staging\.url must be a non-empty HTTPS origin/,
  );
});

test('destructive Playwright cannot be enabled without development endpoints', () => {
  assert.throws(
    () => validateReleaseConfig({
      ...preliveConfig,
      environments: {
        ...preliveConfig.environments,
        production: {
          ...preliveConfig.environments.production,
          enableDevelopmentEndpoints: false,
        },
      },
    }),
    /cannot allow destructive Playwright/,
  );
});

test('environment URLs must be clean HTTPS origins', () => {
  assert.throws(
    () => validateReleaseConfig({
      ...preliveConfig,
      environments: {
        ...preliveConfig.environments,
        production: {
          ...preliveConfig.environments.production,
          url: 'https://app.mrsoftware.dk/login?token=unsafe',
        },
      },
    }),
    /HTTPS origin without credentials, path, query, or fragment/,
  );
});
