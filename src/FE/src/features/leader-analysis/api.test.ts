import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchLeaderAnalysisSummary, fetchLeaderEconomicsSummary } from './api';
import { apiClient } from '../../lib/axios';

vi.mock('../../lib/axios', () => ({
  apiClient: { get: vi.fn() },
}));

describe('leader-analysis api', () => {
  beforeEach(() => vi.clearAllMocks());

  it('maps overview to summary with derived rates', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      activeCount: 4,
      inReviewCount: 2,
      approvedCount: 6,
      rejectedCount: 2,
      recentJobs: [
        { id: '1', reportNumber: '100', status: 'Draft', customer: { name: 'Acme' }, updatedAt: '2026-08-27T10:00:00Z' },
        { id: '2', reportNumber: null, status: 'Approved', customer: null, updatedAt: '2026-08-26T10:00:00Z' },
      ],
    });

    const result = await fetchLeaderAnalysisSummary();

    expect(result.activeCount).toBe(4);
    expect(result.totalCount).toBe(14);
    expect(result.approvalRate).toBeCloseTo(0.75);
    expect(result.rejectionRate).toBeCloseTo(0.25);
    expect(result.recentJobs).toHaveLength(2);
    expect(result.recentJobs[0].customerName).toBe('Acme');
    expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview');
  });

  it('handles empty decided count as null rates', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      activeCount: 1,
      inReviewCount: 1,
      approvedCount: 0,
      rejectedCount: 0,
      recentJobs: [],
    });

    const result = await fetchLeaderAnalysisSummary();

    expect(result.approvalRate).toBeNull();
    expect(result.rejectionRate).toBeNull();
  });

  it('fetches economics summary from leader-analysis endpoint', async () => {
    const economics = {
      providerId: 'economics',
      providerDisplayName: 'e-conomic',
      documentCount: 3,
      invoiceCount: 2,
      receiptCount: 1,
      totalAmount: 9999,
      averageAmount: 3333,
      recentDocuments: [
        { documentId: '1', documentNumber: 'FAK-1', type: 'Invoice', amount: 5000, date: '2026-08-01', status: 'Paid', externalLink: 'https://example.com' },
      ],
    };
    vi.mocked(apiClient.get).mockResolvedValue(economics);

    const result = await fetchLeaderEconomicsSummary();

    expect(result.providerId).toBe('economics');
    expect(result.totalAmount).toBe(9999);
    expect(result.recentDocuments).toHaveLength(1);
    expect(apiClient.get).toHaveBeenCalledWith('/api/leader-analysis/economics/summary');
  });
});
