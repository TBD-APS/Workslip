import { Buffer } from 'node:buffer';
import { expect, test as base } from '@playwright/test';

function readAllowList(name: string): string[] {
  return (process.env[name] ?? '')
    .split(/[\r\n,]+/)
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function isAllowed(value: string, allowList: readonly string[]): boolean {
  return allowList.some((entry) => value.includes(entry));
}

function isApiUrl(value: string): boolean {
  try {
    return new URL(value).pathname.startsWith('/api/');
  } catch {
    return false;
  }
}

export const test = base.extend<{ pageDiagnostics: void }>({
  pageDiagnostics: [
    async ({ page }, use, testInfo) => {
      const issues: string[] = [];
      const allowedConsoleErrors = readAllowList('E2E_ALLOWED_CONSOLE_ERRORS');
      const allowedApiFailures = readAllowList('E2E_ALLOWED_API_FAILURES');

      page.on('pageerror', (error) => {
        const message = `Unhandled page error: ${error.message}`;
        if (!isAllowed(message, allowedConsoleErrors)) issues.push(message);
      });

      page.on('console', (message) => {
        if (message.type() !== 'error') return;
        const entry = `Console error: ${message.text()}`;
        if (!isAllowed(entry, allowedConsoleErrors)) issues.push(entry);
      });

      page.on('requestfailed', (request) => {
        if (!isApiUrl(request.url())) return;
        const entry = `Failed API request: ${request.method()} ${request.url()} (${request.failure()?.errorText ?? 'unknown failure'})`;
        if (!isAllowed(entry, allowedApiFailures)) issues.push(entry);
      });

      page.on('response', (response) => {
        if (!isApiUrl(response.url()) || response.status() < 400) return;
        const entry = `API response error: ${response.request().method()} ${response.url()} (${response.status()})`;
        if (!isAllowed(entry, allowedApiFailures)) issues.push(entry);
      });

      await use();

      const uniqueIssues = [...new Set(issues)];
      if (uniqueIssues.length > 0) {
        await testInfo.attach('browser-diagnostics', {
          body: Buffer.from(uniqueIssues.join('\n'), 'utf8'),
          contentType: 'text/plain',
        });
      }

      expect(uniqueIssues, 'Unexpected browser console or API failures were recorded').toEqual([]);
    },
    { auto: true },
  ],
});

export { expect } from '@playwright/test';
