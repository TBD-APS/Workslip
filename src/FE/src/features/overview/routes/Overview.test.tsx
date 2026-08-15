import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../../lib/axios';
import { Overview } from './Overview';

vi.mock('../../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
  },
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

describe('Overview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
  });

  it('loads the overview once and renders backend counts and recent customer metadata', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      activeCount: 7,
      inReviewCount: 3,
      approvedCount: 11,
      rejectedCount: 2,
      recentJobs: [
        {
          id: '00000000-0000-0000-0000-000000000001',
          reportNumber: '0042',
          status: 'Draft',
          customerName: 'Testkunde A/S',
          customerNumber: 'K-1001',
          address: 'Testvej 1',
          updatedAt: '2026-08-15T12:00:00Z',
        },
      ],
    });

    renderOverview();

    await waitFor(() => expect(apiClient.get).toHaveBeenCalledTimes(1));
    expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview');
    await waitFor(() => {
      expect(screen.getByText('Aktive sager').previousElementSibling).toHaveTextContent('7');
      expect(screen.getByText('Til gennemsyn').previousElementSibling).toHaveTextContent('3');
      expect(screen.getByText('Godkendte sager').previousElementSibling).toHaveTextContent('11');
      expect(screen.getByRole('button', { name: /Se afviste sager/i })).toHaveTextContent('(2)');
      expect(screen.getByText('Testkunde A/S')).toBeInTheDocument();
      expect(screen.getByText('Kundenr. K-1001')).toBeInTheDocument();
    });
  });

  it.each([
    ['Aktive sager', 'Draft'],
    ['Til gennemsyn', 'InReview'],
    ['Godkendte sager', 'Approved'],
  ])('navigates %s to the matching job-list filter', async (label, status) => {
    vi.mocked(apiClient.get).mockResolvedValue({
      activeCount: 1,
      inReviewCount: 1,
      approvedCount: 1,
      rejectedCount: 1,
      recentJobs: [],
    });

    renderOverview();
    const button = await screen.findByRole('button', { name: new RegExp(label, 'i') });
    fireEvent.click(button);

    expect(screen.getByTestId('location')).toHaveTextContent(`/app?status=${status}`);
    expect(sessionStorage.getItem('statusFilter:lastActive')).toBe('mine-jobs');
    expect(sessionStorage.getItem('statusFilter:mine-jobs')).toBe(JSON.stringify([status]));
  });

  it('navigates rejected cases to the rejected job-list filter', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      activeCount: 0,
      inReviewCount: 0,
      approvedCount: 0,
      rejectedCount: 4,
      recentJobs: [],
    });

    renderOverview();
    fireEvent.click(await screen.findByRole('button', { name: /Se afviste sager/i }));

    expect(screen.getByTestId('location')).toHaveTextContent('/app?status=Rejected');
    expect(sessionStorage.getItem('statusFilter:mine-jobs')).toBe(JSON.stringify(['Rejected']));
  });
});
