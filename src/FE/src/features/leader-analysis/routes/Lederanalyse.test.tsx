import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Lederanalyse } from './Lederanalyse';

vi.mock('../../../lib/formatDate', () => ({
  formatDateTimeShort: (value: string) => value,
}));

vi.mock('../../overview/components/AdminPowerBiJobStatusChart', () => ({
  AdminPowerBiJobStatusChart: () => <div data-testid="powerbi-chart">PowerBI</div>,
}));

vi.mock('../../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(async (url: string) => {
      if (url.includes('/worksheets/all/report/power-bi/data')) {
        return { employees: [{ userId: 'u1', employee: 'Test User' }], workHours: [{ userId: 'u1', hours: 8, billableAmount: 1000 }], jobs: [{ status: 'Draft' }, { status: 'Approved' }], customers: [] };
      }
      if (url.includes('/api/jobs')) {
        return { items: [] };
      }
      return { employees: [], workHours: [], jobs: [], customers: [], items: [] };
    }),
  },
}));

const mockFetch = vi.fn();
const mockEconomicsFetch = vi.fn().mockResolvedValue({
  providerId: 'mock',
  providerDisplayName: 'Mock Accounting (Dev)',
  documentCount: 0,
  invoiceCount: 0,
  receiptCount: 0,
  totalAmount: 0,
  averageAmount: 0,
  recentDocuments: [],
});

vi.mock('../api', async () => {
  const actual = await vi.importActual<typeof import('../api')>('../api');
  return {
    ...actual,
    fetchLeaderAnalysisSummary: (...args: unknown[]) => mockFetch(...args),
    fetchLeaderEconomicsSummary: (...args: unknown[]) => mockEconomicsFetch(...args),
    leaderAnalysisQueryKey: ['leader-analysis', 'summary'],
    leaderEconomicsQueryKey: ['leader-analysis', 'economics'],
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
  beforeEach(() => {
    vi.clearAllMocks();
    mockEconomicsFetch.mockResolvedValue({
      providerId: 'mock',
      providerDisplayName: 'Mock Accounting (Dev)',
      documentCount: 0,
      invoiceCount: 0,
      receiptCount: 0,
      totalAmount: 0,
      averageAmount: 0,
      recentDocuments: [],
    });
  });

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

  it('renders Power BI visualization as primary element (moved from Overblik)', async () => {
    mockFetch.mockResolvedValue({
      activeCount: 1,
      inReviewCount: 1,
      approvedCount: 1,
      rejectedCount: 1,
      totalCount: 4,
      approvalRate: 0.5,
      rejectionRate: 0.5,
      recentJobs: [],
    });

    renderWithProviders();

    await waitFor(() => expect(screen.getByTestId('powerbi-chart')).toBeInTheDocument());
    expect(document.getElementById('leader-analysis-powerbi')).toBeInTheDocument();
  });

  it('renders economics money and bilag from e-conomic', async () => {
    mockFetch.mockResolvedValue({
      activeCount: 2,
      inReviewCount: 2,
      approvedCount: 2,
      rejectedCount: 2,
      totalCount: 8,
      approvalRate: 0.5,
      rejectionRate: 0.5,
      recentJobs: [],
    });
    mockEconomicsFetch.mockResolvedValue({
      providerId: 'economics',
      providerDisplayName: 'e-conomic',
      documentCount: 5,
      invoiceCount: 3,
      receiptCount: 2,
      totalAmount: 12345,
      averageAmount: 2469,
      recentDocuments: [
        { documentId: '1', documentNumber: 'FAK-1001', type: 'Invoice', amount: 5000, date: '2026-08-01', status: 'Paid', externalLink: 'https://example.com/1' },
        { documentId: '2', documentNumber: 'BIL-2001', type: 'Receipt', amount: 7345, date: '2026-08-02', status: 'Pending', externalLink: 'https://example.com/2' },
      ],
    });

    renderWithProviders();

    await waitFor(() => expect(screen.getByText('Økonomi & bilag')).toBeInTheDocument());
    await waitFor(() => expect(screen.getByText(/5\.000/)).toBeInTheDocument());
    expect(document.getElementById('leader-analysis-economics')).toBeInTheDocument();
    expect(screen.getByText('FAK-1001')).toBeInTheDocument();
    expect(screen.getByText('BIL-2001')).toBeInTheDocument();
    expect(document.getElementById('leader-economics-provider-badge')).toHaveTextContent('e-conomic');
  });

  it('renders bemanding, sagsøkonomi, SLA and export panels', async () => {
    mockFetch.mockResolvedValue({
      activeCount: 1,
      inReviewCount: 1,
      approvedCount: 1,
      rejectedCount: 1,
      totalCount: 4,
      approvalRate: 0.5,
      rejectionRate: 0.5,
      recentJobs: [],
    });

    renderWithProviders();

    await waitFor(() => expect(document.getElementById('leader-analysis-bemanding')).toBeInTheDocument());
    expect(document.getElementById('leader-analysis-sagsokonomi')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-sla')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-export')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-export-csv')).toBeInTheDocument();
    expect(document.getElementById('leader-analysis-export-pdf')).toBeInTheDocument();
  });

  it('renders interactive visuals panel for graphs and maps', async () => {
    mockFetch.mockResolvedValue({
      activeCount: 2,
      inReviewCount: 2,
      approvedCount: 2,
      rejectedCount: 2,
      totalCount: 8,
      approvalRate: 0.5,
      rejectionRate: 0.5,
      recentJobs: [],
    });

    renderWithProviders();

    await waitFor(() => expect(document.getElementById('leader-analysis-visuals')).toBeInTheDocument());
    expect(document.getElementById('visuals-datasource')).toBeInTheDocument();
    expect(document.getElementById('visuals-charttype')).toBeInTheDocument();
    expect(document.getElementById('visuals-chart')).toBeInTheDocument();
    expect(document.getElementById('visuals-map')).toBeInTheDocument();
  });
});
