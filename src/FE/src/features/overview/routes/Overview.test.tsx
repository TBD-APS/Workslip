import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../../lib/axios';
import { Overview } from './Overview';

vi.mock('../../../lib/axios', () => ({ apiClient: { get: vi.fn() } }));

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{`${location.pathname}${location.search}`}</output>;
}

function renderOverview() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}><MemoryRouter initialEntries={['/app/overblik']}><Overview /><LocationProbe /></MemoryRouter></QueryClientProvider>);
}

describe('Overview', () => {
  beforeEach(() => { vi.clearAllMocks(); sessionStorage.clear(); });

  it('renders backend counts and recent customer metadata', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ activeCount: 7, inReviewCount: 3, approvedCount: 11, rejectedCount: 2, recentJobs: [{ id: '00000000-0000-0000-0000-000000000001', reportNumber: '0042', status: 'Draft', customerName: 'Testkunde A/S', customerNumber: 'K-1001', address: 'Testvej 1', updatedAt: '2026-08-15T12:00:00Z' }] });
    renderOverview();
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith('/api/jobs/overview'));
    expect(await screen.findByText('Testkunde A/S')).toBeInTheDocument();
    expect(screen.getByText('Kundenr. K-1001')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Se afviste sager/i })).toHaveTextContent('(2)');
  });

  it.each([['Aktive sager', 'Draft'], ['Til gennemsyn', 'InReview'], ['Godkendte sager', 'Approved']])('navigates %s to matching status', async (label, status) => {
    vi.mocked(apiClient.get).mockResolvedValue({ activeCount: 1, inReviewCount: 1, approvedCount: 1, rejectedCount: 1, recentJobs: [] });
    renderOverview();
    fireEvent.click(await screen.findByRole('button', { name: new RegExp(label, 'i') }));
    expect(screen.getByTestId('location')).toHaveTextContent(`/app?status=${status}`);
    expect(sessionStorage.getItem('statusFilter:mine-jobs')).toBe(JSON.stringify([status]));
  });

  it('navigates rejected cases to rejected filter', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ activeCount: 0, inReviewCount: 0, approvedCount: 0, rejectedCount: 4, recentJobs: [] });
    renderOverview();
    fireEvent.click(await screen.findByRole('button', { name: /Se afviste sager/i }));
    expect(screen.getByTestId('location')).toHaveTextContent('/app?status=Rejected');
    expect(sessionStorage.getItem('statusFilter:mine-jobs')).toBe(JSON.stringify(['Rejected']));
  });

  it('keeps semantic status classes for the requested overview colors', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ activeCount: 1, inReviewCount: 1, approvedCount: 1, rejectedCount: 1, recentJobs: [] });
    renderOverview();
    expect(await screen.findByRole('button', { name: /Aktive sager/i })).toHaveClass('overview-status-card--active');
    expect(screen.getByRole('button', { name: /Til gennemsyn/i })).toHaveClass('overview-status-card--review');
    expect(screen.getByRole('button', { name: /Godkendte sager/i })).toHaveClass('overview-status-card--approved');
    expect(screen.getByRole('button', { name: /Se afviste sager/i })).toHaveClass('overview-rejected-cta');
  });
});
