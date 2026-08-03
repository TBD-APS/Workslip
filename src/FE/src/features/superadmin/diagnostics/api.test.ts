import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../../lib/axios';
import { getErrorDiagnostics, parseErrorDiagnosticsDashboard } from './api';

vi.mock('../../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

const validDashboard = {
  isAvailable: true,
  isComplete: true,
  isStale: false,
  availabilityReason: null,
  generatedAtUtc: '2026-08-02T05:00:00Z',
  dataRetrievedAtUtc: '2026-08-02T05:00:00Z',
  summaryAvailable: true,
  itemsAvailable: true,
  telemetryHealthAvailable: true,
  hasPartialAzureResults: false,
  isTruncated: false,
  summary: {
    lastHour: 1,
    last24Hours: 2,
    last7Days: 3,
    frontendLast24Hours: 1,
    backendLast24Hours: 1,
  },
  telemetryHealth: {
    frontendLastSeenUtc: '2026-08-02T04:58:00Z',
    backendLastSeenUtc: '2026-08-02T04:59:00Z',
  },
  items: [{
    timestampUtc: '2026-08-02T04:59:00Z',
    firstSeenUtc: '2026-08-01T18:00:00Z',
    lastSeenUtc: '2026-08-02T04:59:00Z',
    source: 'backend',
    severity: 'error',
    errorType: 'SqlException',
    fingerprint: 'abcdef123456',
    message: 'Database operation failed',
    route: '/api/jobs/:id',
    operation: 'POST /api/jobs/:id',
    release: 'release-2',
    correlationId: 'abcdefabcdef1234',
    traceId: null,
    affectedReleaseCount: 2,
    affectedRouteCount: 3,
    affectedOperationCount: 2,
    occurrences: 7,
  }],
};

describe('Error diagnostics API', () => {
  beforeEach(() => vi.clearAllMocks());

  it('validates the grouped dashboard response before exposing it to the UI', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(validDashboard);

    await expect(getErrorDiagnostics('24h', 'all')).resolves.toEqual(validDashboard);
    expect(apiClient.get).toHaveBeenCalledWith('/api/admin/diagnostics/errors', {
      params: { range: '24h', source: 'all', limit: 50 },
      skipGlobalErrorToast: true,
    });
  });

  it('rejects a group whose first seen timestamp is after last seen', () => {
    expect(() => parseErrorDiagnosticsDashboard({
      ...validDashboard,
      items: [{
        ...validDashboard.items[0],
        firstSeenUtc: '2026-08-02T05:00:00Z',
      }],
    })).toThrow('Logdashboardet modtog et ugyldigt svar');
  });

  it('rejects a group whose representative timestamp is not last seen', () => {
    expect(() => parseErrorDiagnosticsDashboard({
      ...validDashboard,
      items: [{
        ...validDashboard.items[0],
        timestampUtc: '2026-08-02T04:58:00Z',
      }],
    })).toThrow('Logdashboardet modtog et ugyldigt svar');
  });

  it('rejects invalid grouped context counts', () => {
    expect(() => parseErrorDiagnosticsDashboard({
      ...validDashboard,
      items: [{
        ...validDashboard.items[0],
        affectedReleaseCount: -1,
      }],
    })).toThrow('Logdashboardet modtog et ugyldigt svar');
  });

  it('rejects inconsistent responses instead of displaying false zeroes', () => {
    expect(() => parseErrorDiagnosticsDashboard({
      ...validDashboard,
      summaryAvailable: true,
      summary: null,
    })).toThrow('Logdashboardet modtog et ugyldigt svar');
  });

  it('rejects a complete response without validated telemetry health', () => {
    expect(() => parseErrorDiagnosticsDashboard({
      ...validDashboard,
      telemetryHealthAvailable: false,
      telemetryHealth: null,
    })).toThrow('Logdashboardet modtog et ugyldigt svar');
  });

  it('accepts explicit null timestamps when the health query found no telemetry', () => {
    expect(parseErrorDiagnosticsDashboard({
      ...validDashboard,
      telemetryHealth: {
        frontendLastSeenUtc: null,
        backendLastSeenUtc: null,
      },
    }).telemetryHealth).toEqual({
      frontendLastSeenUtc: null,
      backendLastSeenUtc: null,
    });
  });

  it('accepts an explicitly unavailable response without manufacturing data', () => {
    expect(parseErrorDiagnosticsDashboard({
      isAvailable: false,
      isComplete: false,
      isStale: false,
      availabilityReason: 'query_failed',
      generatedAtUtc: '2026-08-02T05:00:00Z',
      dataRetrievedAtUtc: null,
      summaryAvailable: false,
      itemsAvailable: false,
      telemetryHealthAvailable: false,
      hasPartialAzureResults: false,
      isTruncated: false,
      summary: null,
      telemetryHealth: null,
      items: [],
    })).toMatchObject({
      isAvailable: false,
      summary: null,
      telemetryHealth: null,
      items: [],
    });
  });
});
