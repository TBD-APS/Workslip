import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Lederanalyse } from './Lederanalyse';

vi.mock('../../../lib/formatDate', () => ({
  formatDateTimeShort: (value: string) => value,
}));

const mockFetch = vi.fn();

vi.mock('../api', async () => {
  const actual = await vi.importActual<typeof import('../api')>('../api');
  return {
    ...actual,
    fetchLeaderAnalysisSummary: (...args: unknown[]) => mockFetch(...args),
    leaderAnalysisQueryKey: ['leader-analysis', 'summary'],
  };
});

function renderWithProviders() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <Lederanalyse />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Lederanalyse', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders KPI cards from summary', async () => {
    mockFetch.mockResolvedValue({
      activeCount: 7,
      inReviewCount: 5,
      approvedCount: 11,
      rejectedCount: 2,
      totalCount: 25,
      approvalRate: 0.846,
      rejectionRate: 0.154,
      recentJobs: [
        { id: 'job-1', reportNumber: '001', status: 'Draft', customerName: 'Alpha', updatedAt: '2026-08-27T10:00:00Z' },
      ],
    });

    renderWithProviders();

    await waitFor(() => expect(screen.getByText('Lederanalyse')).toBeInTheDocument());

    expect(document.getElementById('leader-analysis-kpi-active')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-kpi-review')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-kpi-approved')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-kpi-rejected')).toBeInTheDocument();

    await waitFor(() => expect(document.getElementById('leader-analysis-kpi-active')).toHaveTextContent('7'));
    expect(document.getElementById('leader-analysis-kpi-review')).toHaveTextContent('5');
    expect(document.getElementById('leader-analysis-kpi-approved')).toHaveTextContent('11');
    expect(document.getElementById('leader-analysis-kpi-rejected')).toHaveTextContent('2');
  });

  it('shows risk banner when thresholds exceeded', async () => {
    mockFetch.mockResolvedValue({
      activeCount: 3,
      inReviewCount: 6,
      approvedCount: 2,
      rejectedCount: 1,
      totalCount: 12,
      approvalRate: 0.66,
      rejectionRate: 0.33,
      recentJobs: [],
    });

    renderWithProviders();

    await waitFor(() => expect(document.getElementById('leader-analysis-risk-banner')).toBeInTheDocument());
  });

  it('has stable IDs for Playwright contract', async () => {
    mockFetch.mockResolvedValue({
      activeCount: 0,
      inReviewCount: 0,
      approvedCount: 0,
      rejectedCount: 0,
      totalCount: 0,
      approvalRate: null,
      rejectionRate: null,
      recentJobs: [],
    });

    renderWithProviders();

    await waitFor(() => expect(document.getElementById('leader-analysis-page')).toBeInTheDocument());
    expect(document.getElementById('leader-analysis-go-overview')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-flow-panel')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-quality-panel')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-recent')).toBeInTheDocument();
  });
});
