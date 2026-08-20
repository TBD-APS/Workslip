import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../../lib/axios';
import { useHasRole } from '../../../providers/permissions/usePermissions';
import { Overview } from './Overview';

vi.mock('../../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

vi.mock('../../../providers/permissions/usePermissions', () => ({
  useHasRole: vi.fn(),
}));

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{`${location.pathname}${location.search}`}</output>;
}

function renderOverview() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/app/overblik']}>
        <Overview />
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const overviewResponse = {
  activeCount: 7,
  inReviewCount: 3,
  approvedCount: 11,
  rejectedCount: 2,
  recentJobs: [],
};

describe('Overview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    vi.mocked(useHasRole).mockReturnValue(false);
  });

  it('loads the overview once and renders the backend status counts', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(overviewResponse);

    renderOverview();

    await waitFor(() => expect(apiClient.get).toHaveBeenCalledTimes(1));
    expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview');
    await waitFor(() => {
      expect(screen.getByText('Aktive sager').previousElementSibling).toHaveTextContent('7');
      expect(screen.getByText('Til gennemsyn').previousElementSibling).toHaveTextContent('3');
      expect(screen.getByText('Godkendte sager').previousElementSibling).toHaveTextContent('11');
      expect(screen.getByRole('button', { name: /2 afviste/i })).toBeInTheDocument();
    });
  });

  it('does not fetch or render Power BI analytics for non-admin users', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(overviewResponse);

    renderOverview();

    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview'));
    expect(apiClient.get).not.toHaveBeenCalledWith(
      '/api/power-bi/overview/job-status',
      expect.anything(),
    );
    expect(screen.queryByTestId('admin-power-bi-job-status')).not.toBeInTheDocument();
  });

  it('renders the Power BI donut and fetches its summary only for Admin', async () => {
    vi.mocked(useHasRole).mockReturnValue(true);
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/jobs/overview') return overviewResponse;
      if (url === '/api/power-bi/overview/job-status') {
        return {
          total: 10,
          draft: 4,
          inReview: 2,
          approved: 3,
          rejected: 1,
          other: 0,
          generatedAtUtc: '2026-08-20T17:00:00Z',
        };
      }
      throw new Error(`Unexpected URL: ${url}`);
    });

    renderOverview();

    expect(await screen.findByTestId('admin-power-bi-job-status')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /Sagsfordeling/i })).toHaveAccessibleName(
      /Aktive: 4, Til gennemsyn: 2, Godkendte: 3, Afviste: 1/i,
    );
    expect(screen.getByText('10')).toBeInTheDocument();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/power-bi/overview/job-status',
      { skipGlobalErrorToast: true },
    );
  });

  it.each([
    ['Aktive sager', 'Draft'],
    ['Til gennemsyn', 'InReview'],
    ['Godkendte sager', 'Approved'],
  ])('navigates %s to its explicit status filter', async (label, status) => {
    vi.mocked(apiClient.get).mockResolvedValue({
      activeCount: 1,
      inReviewCount: 1,
      approvedCount: 1,
      rejectedCount: 1,
      recentJobs: [],
    });

    renderOverview();

    fireEvent.click(await screen.findByRole('button', { name: new RegExp(label, 'i') }));

    expect(screen.getByTestId('location')).toHaveTextContent(`/app?status=${status}`);
    expect(sessionStorage.getItem('statusFilter:mine-jobs')).toBe(JSON.stringify([status]));
  });

  it('routes rejected cases to the rejected filter', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      activeCount: 0,
      inReviewCount: 0,
      approvedCount: 0,
      rejectedCount: 4,
      recentJobs: [],
    });

    renderOverview();

    fireEvent.click(await screen.findByRole('button', { name: /4 afviste/i }));

    expect(screen.getByTestId('location')).toHaveTextContent('/app?status=Rejected');
    expect(sessionStorage.getItem('statusFilter:mine-jobs')).toBe(JSON.stringify(['Rejected']));
  });
});
