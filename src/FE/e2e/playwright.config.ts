import { defineConfig, devices } from '@playwright/test';

const externalBaseUrl = process.env.E2E_BASE_URL?.trim();
const localBaseUrl = 'http://127.0.0.1:5270';
const baseURL = externalBaseUrl || localBaseUrl;

export default defineConfig({
  testDir: './tests',
  outputDir: './test-results',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 1 : undefined,
  timeout: 45_000,
  expect: {
    timeout: 10_000,
  },
  reporter: process.env.CI
    ? [
        ['line'],
        ['html', { open: 'never', outputFolder: 'playwright-report' }],
      ]
    : [
        ['list'],
        ['html', { open: 'never', outputFolder: 'playwright-report' }],
      ],
  use: {
    baseURL,
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 10_000,
    navigationTimeout: 20_000,
  },
  projects: [
    {
      name: 'chromium-desktop',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 1000 },
      },
    },
    {
      name: 'chromium-mobile',
      use: {
        ...devices['Pixel 7'],
      },
    },
  ],
  webServer: externalBaseUrl
    ? undefined
    : {
        command: 'npm --prefix .. run dev -- --host 127.0.0.1 --port 5270',
        url: `${localBaseUrl}/login`,
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
        env: {
          ...process.env,
          VITE_ENABLE_DEV_LOGIN: 'false',
        },
      },
});
