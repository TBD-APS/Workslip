import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
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
const analyticsResponse = {
  generatedAtUtc: '2026-08-20T17:00:00Z',
  employees: [{ userId: 'u1', employee: 'Ada', role: 'User' }],
  workHours: [{ userId: 'u1', hours: 8, billableAmount: 6400 }],
  jobs: [
    ...Array.from({ length: 4 }, () => ({ status: 'Draft' })),
    ...Array.from({ length: 2 }, () => ({ status: 'InReview' })),
    ...Array.from({ length: 3 }, () => ({ status: 'Approved' })),
    { status: 'Rejected' },
  ],
  customers: [{ customerId: 'c1', customer: 'Aarhus VVS', createdDate: '2026-08-01' }],
};

describe('Overview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    vi.mocked(useHasRole).mockReturnValue(false);
    vi.mocked(getApiCustomersFavorite).mockResolvedValue([]);
    vi.mocked(listDocuments).mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('renders all four live status counts', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(overviewResponse);
    renderOverview();
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview'));
    await waitFor(() => {
      expect(screen.getByText('Aktive sager').previousElementSibling).toHaveTextContent('7');
      expect(screen.getByText('Til gennemsyn').previousElementSibling).toHaveTextContent('3');
      expect(screen.getByText('Godkendte sager').previousElementSibling).toHaveTextContent('11');
      expect(screen.getByText('Afviste sager').previousElementSibling).toHaveTextContent('2');
    });
  });

  it('does not fetch Admin dashboard data for non-admin users', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(overviewResponse);
    renderOverview();
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview'));
    expect(apiClient.get).not.toHaveBeenCalledWith('/api/worksheets/all/report/power-bi/data?historyMonths=24', expect.anything());
    expect(getApiCustomersFavorite).not.toHaveBeenCalled();
    expect(listDocuments).not.toHaveBeenCalled();
    expect(screen.queryByTestId('admin-power-bi-job-status')).not.toBeInTheDocument();
  });

  it('renders analytics tabs, favorite customers and latest documents for Admin', async () => {
    vi.mocked(useHasRole).mockReturnValue(true);
    vi.mocked(getApiCustomersFavorite).mockResolvedValue([{ id: 'c1', customerNumber: '1', name: 'Aarhus VVS', email: null, phone: null, address: null, zipCode: null, city: 'Aarhus', country: null, contactPerson: null, isFavorite: true }]);
    vi.mocked(listDocuments).mockResolvedValue({ items: [{ id: 'd1', title: 'KLS skabelon', preview: '', tags: [], updatedAt: '2026-08-20T10:00:00Z', updatedByDisplayName: 'Admin', revision: 1 }], totalCount: 1 });
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/jobs/overview') return overviewResponse;
      if (url === '/api/worksheets/all/report/power-bi/data?historyMonths=24') return analyticsResponse;
      throw new Error(`Unexpected URL: ${url}`);
    });
    renderOverview();

    expect(await screen.findByTestId('admin-power-bi-job-status')).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: /Sagsfordeling/i })).toHaveAccessibleName(/Aktive: 4, Til gennemsyn: 2, Godkendte: 3, Afviste: 1/i);
    expect(await screen.findByText('Aarhus VVS')).toBeInTheDocument();
    expect(await screen.findByText('KLS skabelon')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Medarbejderøkonomi/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Nye kunder/i })).toBeInTheDocument();
  });

  it.each([
    ['Aktive sager', 'Draft'],
    ['Til gennemsyn', 'InReview'],
    ['Godkendte sager', 'Approved'],
    ['Afviste sager', 'Rejected'],
  ])('navigates %s to its explicit status filter', async (label, status) => {
    vi.mocked(apiClient.get).mockResolvedValue({ ...overviewResponse, activeCount: 1, inReviewCount: 1, approvedCount: 1, rejectedCount: 1 });
    renderOverview();
    fireEvent.click(await screen.findByRole('button', { name: new RegExp(label, 'i') }));
    expect(screen.getByTestId('location')).toHaveTextContent(`/app?status=${status}`);
    expect(sessionStorage.getItem('statusFilter:mine-jobs')).toBe(JSON.stringify([status]));
  });
});
