import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../../lib/axios';
import { Overview } from './Overview';

vi.mock('../../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

function renderOverview() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <Overview />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Overview', () => {
  beforeEach(() => vi.clearAllMocks());

  it('loads the overview once and renders the backend status counts', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      activeCount: 7,
      inReviewCount: 3,
      approvedCount: 11,
      rejectedCount: 2,
      recentJobs: [],
    });

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
});
