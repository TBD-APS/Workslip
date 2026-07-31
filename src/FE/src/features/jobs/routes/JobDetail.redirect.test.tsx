import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { JobStatus } from '../../../api/generated/models';
import { JobDetail } from './JobDetail';
import { useJobDetails } from '../hooks/useJobDetails';

vi.mock('../hooks/useJobDetails', () => ({
  useJobDetails: vi.fn(),
}));

vi.mock('../components/JobDetails', () => ({
  JobDetailsPage: () => <div>edit-flow</div>,
}));

vi.mock('../../../providers/permissions/usePermissions', () => ({
  useIsAdmin: () => false,
}));

vi.mock('../../../hooks/useScrollRestore', () => ({
  useScrollRestore: () => undefined,
}));

vi.mock('../utils/markJobSeen', () => ({
  markJobAsSeen: () => undefined,
}));

const useJobDetailsMock = vi.mocked(useJobDetails);

afterEach(() => cleanup());

function renderAt(route: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <Routes>
          <Route path="/app/job/:id" element={<JobDetail />} />
          <Route path="/app/completed/:id" element={<div>completed-view</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('JobDetail routing for job type', () => {
  it('redirects a Diverse job to the completed/review view instead of the edit flow', () => {
    useJobDetailsMock.mockReturnValue({
      job: { id: 'diverse-1', jobType: 'Diverse', status: JobStatus.InReview },
      currentStep: 0,
      setCurrentStep: vi.fn(),
    } as never);

    renderAt('/app/job/diverse-1');

    expect(screen.getByText('completed-view')).toBeInTheDocument();
    expect(screen.queryByText('edit-flow')).not.toBeInTheDocument();
  });

  it('keeps a KLS job in the edit flow', () => {
    useJobDetailsMock.mockReturnValue({
      job: { id: 'kls-1', jobType: 'KLS', status: JobStatus.Draft },
      currentStep: 0,
      setCurrentStep: vi.fn(),
    } as never);

    renderAt('/app/job/kls-1');

    expect(screen.getByText('edit-flow')).toBeInTheDocument();
    expect(screen.queryByText('completed-view')).not.toBeInTheDocument();
  });
});
