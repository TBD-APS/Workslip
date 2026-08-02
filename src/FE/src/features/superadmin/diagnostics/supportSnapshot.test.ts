import { describe, expect, it } from 'vitest';
import type { ErrorDiagnosticsDashboard } from './types';
import { serializeErrorDiagnosticsSupportSnapshot } from './supportSnapshot';

const dashboard: ErrorDiagnosticsDashboard = {
  isAvailable: true,
  isComplete: false,
  isStale: true,
  availabilityReason: 'timeout',
  generatedAtUtc: '2026-08-02T20:00:00.000Z',
  dataRetrievedAtUtc: '2026-08-02T19:55:00.000Z',
  summaryAvailable: true,
  itemsAvailable: true,
  telemetryHealthAvailable: false,
  hasPartialAzureResults: true,
  isTruncated: true,
  summary: {
    lastHour: 2,
    last24Hours: 8,
    last7Days: 17,
    frontendLast24Hours: 3,
    backendLast24Hours: 5,
  },
  telemetryHealth: null,
  items: [
    {
      timestampUtc: '2026-08-02T19:50:00.000Z',
      source: 'backend',
      severity: 'error',
      errorType: 'HTTP 500',
      fingerprint: 'a1b2c3d4',
      message: 'Backend request failed',
      route: '/api/jobs/{id}',
      operation: 'POST /api/jobs/{id}/status',
      release: '2026.08.02.1',
      correlationId: 'abc123',
      traceId: 'def456',
      occurrences: 2,
    },
  ],
};

describe('serializeErrorDiagnosticsSupportSnapshot', () => {
  it('preserves filters and incomplete-data warnings', () => {
    const result = JSON.parse(serializeErrorDiagnosticsSupportSnapshot(
      dashboard,
      '24h',
      'backend',
      '2026-08-02T21:00:00.000Z',
    ));

    expect(result).toMatchObject({
      schemaVersion: 1,
      source: 'workslip-superadmin-error-diagnostics',
      exportedAtUtc: '2026-08-02T21:00:00.000Z',
      filters: { range: '24h', source: 'backend' },
      dashboard: {
        isAvailable: true,
        isComplete: false,
        isStale: true,
        availabilityReason: 'timeout',
        hasPartialAzureResults: true,
        isTruncated: true,
      },
    });
  });

  it('copies only allowlisted dashboard fields', () => {
    const dashboardWithUnexpectedFields = {
      ...dashboard,
      rawAzureResponse: 'must-not-leak',
      items: dashboard.items.map((item) => ({
        ...item,
        rawStackTrace: 'must-not-leak',
        requestBody: 'must-not-leak',
      })),
    } as ErrorDiagnosticsDashboard;

    const result = serializeErrorDiagnosticsSupportSnapshot(
      dashboardWithUnexpectedFields,
      '7d',
      'all',
      '2026-08-02T21:00:00.000Z',
    );

    expect(result).not.toContain('rawAzureResponse');
    expect(result).not.toContain('rawStackTrace');
    expect(result).not.toContain('requestBody');
    expect(result).not.toContain('must-not-leak');
  });
});
