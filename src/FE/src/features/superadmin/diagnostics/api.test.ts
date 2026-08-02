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
  hasPartialAzureResults: false,
  isTruncated: false,
  summary: {
    lastHour: 1,
    last24Hours: 2,
    last7Days: 3,
    frontendLast24Hours: 1,
    backendLast24Hours: 1,
  },
  items: [{
    timestampUtc: '2026-08-02T04:59:00Z',
    source: 'backend',
    severity: 'error',
    errorType: 'SqlException',
    fingerprint: 'abcdef123456',
    message: 'Database operation failed',
    route: '/api/jobs/:id',
    operation: 'POST /api/jobs/:id',
    release: 'release-1',
    correlationId: 'correlation-1',
    traceId: null,
    occurrences: 2,
  }],
};

describe('Error diagnostics API', () => {
  beforeEach(() => vi.clearAllMocks());

  it('validates the dashboard response before exposing it to the UI', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(validDashboard);

    await expect(getErrorDiagnostics('24h', 'all')).resolves.toEqual(validDashboard);
    expect(apiClient.get).toHaveBeenCalledWith('/api/admin/diagnostics/errors', {
      params: { range: '24h', source: 'all', limit: 50 },
      skipGlobalErrorToast: true,
    });
  });

  it('rejects inconsistent responses instead of displaying false zeroes', () => {
    expect(() => parseErrorDiagnosticsDashboard({
      ...validDashboard,
      summaryAvailable: true,
      summary: null,
    })).toThrow('Logdashboardet modtog et ugyldigt svar');
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
      hasPartialAzureResults: false,
      isTruncated: false,
      summary: null,
      items: [],
    })).toMatchObject({
      isAvailable: false,
      summary: null,
      items: [],
    });
  });
});
