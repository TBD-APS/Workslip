import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../../lib/axios';
import { useHasRole } from '../../../providers/permissions/usePermissions';
import { listDocuments } from '../../docs/docsApi';
import { getApiCustomersFavorite } from '../../../api/generated/customers/customers';
import { Overview } from './Overview';

vi.mock('../../../lib/axios', () => ({ apiClient: { get: vi.fn() } }));
vi.mock('../../../providers/permissions/usePermissions', () => ({ useHasRole: vi.fn() }));
vi.mock('../../../api/generated/customers/customers', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  getApiCustomersFavorite: vi.fn(),
}));
vi.mock('../../docs/docsApi', () => ({ listDocuments: vi.fn() }));

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{`${location.pathname}${location.search}`}</output>;
}

function renderOverview() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}><MemoryRouter initialEntries={['/app/overblik']}><Overview /><LocationProbe /></MemoryRouter></QueryClientProvider>);
}

const overviewResponse = { activeCount: 7, inReviewCount: 3, approvedCount: 11, rejectedCount: 2, recentJobs: [] };

describe('Overview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    vi.mocked(useHasRole).mockReturnValue(false);
    vi.mocked(getApiCustomersFavorite).mockResolvedValue([]);
    vi.mocked(listDocuments).mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('renders recent jobs without status counts (statistics moved to Lederanalyse)', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(overviewResponse);
    renderOverview();
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview'));
    await waitFor(() => expect(screen.getByText('Seneste sager')).toBeInTheDocument());
    expect(screen.queryByText('Aktive sager')).not.toBeInTheDocument();
    expect(screen.queryByText('Til gennemsyn')).not.toBeInTheDocument();
    expect(screen.queryByTestId('admin-power-bi-job-status')).not.toBeInTheDocument();
  });

  it('does not fetch Power BI data on Overblik (now in Lederanalyse)', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(overviewResponse);
    renderOverview();
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview'));
    expect(apiClient.get).not.toHaveBeenCalledWith('/api/worksheets/all/report/power-bi/data?historyMonths=24', expect.anything());
    expect(screen.queryByTestId('admin-power-bi-job-status')).not.toBeInTheDocument();
  });

  it('renders Lederanalyse card, favorite customers and latest documents for Admin', async () => {
    vi.mocked(useHasRole).mockReturnValue(true);
    vi.mocked(getApiCustomersFavorite).mockResolvedValue([{ id: 'c1', customerNumber: '1', name: 'Aarhus VVS', email: null, phone: null, address: null, zipCode: null, city: 'Aarhus', country: null, contactPerson: null, isFavorite: true }]);
    vi.mocked(listDocuments).mockResolvedValue({ items: [{ id: 'd1', title: 'KLS skabelon', preview: '', tags: [], updatedAt: '2026-08-20T10:00:00Z', updatedByDisplayName: 'Admin', revision: 1 }], totalCount: 1 });
    vi.mocked(apiClient.get).mockResolvedValue(overviewResponse);
    renderOverview();

    expect(await screen.findByText('Lederanalyse')).toBeInTheDocument();
    expect(await screen.findByText('Se driftsnøgletal')).toBeInTheDocument();
    expect(await screen.findByText('Aarhus VVS')).toBeInTheDocument();
    expect(await screen.findByText('KLS skabelon')).toBeInTheDocument();
    expect(screen.queryByTestId('admin-power-bi-job-status')).not.toBeInTheDocument();
  });
});
