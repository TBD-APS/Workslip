import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JobStatus } from '../../../api/generated/models';
import { JobDetail } from './JobDetail';

const mocks = vi.hoisted(() => ({
  isAdmin: false,
  markJobAsSeen: vi.fn(),
  useJobDetails: vi.fn(),
}));

vi.mock('../hooks/useJobDetails', () => ({
  useJobDetails: mocks.useJobDetails,
}));

vi.mock('../components/JobDetails', () => ({
  JobDetailsPage: ({ details }: { details: { currentStep: number } }) => (
    <div>Wizard step {details.currentStep}</div>
  ),
}));

vi.mock('../utils/markJobSeen', () => ({
  markJobAsSeen: mocks.markJobAsSeen,
}));

vi.mock('../../../hooks/useScrollRestore', () => ({
  useScrollRestore: vi.fn(),
}));

vi.mock('../../../providers/permissions/usePermissions', () => ({
  useIsAdmin: () => mocks.isAdmin,
}));

type DetailsStub = {
  job: { id: string; status: JobStatus };
  currentStep: number;
  setCurrentStep: ReturnType<typeof vi.fn>;
};

function createDetails(
  jobId: string,
  status: JobStatus,
  currentStep: number,
): DetailsStub {
  return {
    job: { id: jobId, status },
    currentStep,
    setCurrentStep: vi.fn(),
  };
}

function createTestTree(queryClient: QueryClient): ReactNode {
  return (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/app/jobs/job-1']}>
        <div className="app-shell">
          <RouteControls />
          <Routes>
            <Route path="/app/jobs/:id" element={<JobDetail />} />
            <Route path="/app/completed/:id" element={<div>Completed report</div>} />
          </Routes>
        </div>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function RouteControls() {
  const navigate = useNavigate();

  return (
    <>
      <button type="button" onClick={() => navigate('/app/jobs/job-1')}>Open job 1</button>
      <button type="button" onClick={() => navigate('/app/jobs/job-2')}>Open job 2</button>
    </>
  );
}

describe('JobDetail rejected-job landing', () => {
  let scrollTo: ReturnType<typeof vi.fn>;

  afterEach(cleanup);

  beforeEach(() => {
    vi.clearAllMocks();
    mocks.isAdmin = false;
    scrollTo = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
      configurable: true,
      value: scrollTo,
    });
  });

  it('normalizes each rejected job once without overriding later wizard navigation', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const firstJob = createDetails('job-1', JobStatus.Rejected, 4);
    let details = firstJob;
    mocks.useJobDetails.mockImplementation(() => details);

    const view = render(createTestTree(queryClient));

    await waitFor(() => expect(firstJob.setCurrentStep).toHaveBeenCalledOnce());
    expect(firstJob.setCurrentStep).toHaveBeenCalledWith(expect.any(Function));
    expect(firstJob.setCurrentStep.mock.calls[0][0](4)).toBe(0);
    expect(scrollTo).toHaveBeenCalledOnce();
    expect(mocks.markJobAsSeen).toHaveBeenCalledOnce();
    expect(mocks.markJobAsSeen).toHaveBeenCalledWith('job-1', queryClient);

    firstJob.setCurrentStep.mockClear();
    details = { ...firstJob, currentStep: 2 };
    view.rerender(createTestTree(queryClient));

    expect(screen.getByText('Wizard step 2')).toBeInTheDocument();
    expect(firstJob.setCurrentStep).not.toHaveBeenCalled();
    expect(scrollTo).toHaveBeenCalledOnce();

    fireEvent.click(screen.getByRole('button', { name: 'Open job 2' }));
    expect(firstJob.setCurrentStep).not.toHaveBeenCalled();

    const secondJob = createDetails('job-2', JobStatus.Rejected, 3);
    details = secondJob;
    view.rerender(createTestTree(queryClient));

    await waitFor(() => expect(secondJob.setCurrentStep).toHaveBeenCalledOnce());
    expect(secondJob.setCurrentStep.mock.calls[0][0](3)).toBe(0);
    expect(scrollTo).toHaveBeenCalledTimes(2);

    fireEvent.click(screen.getByRole('button', { name: 'Open job 1' }));
    details = { ...firstJob, currentStep: 2 };
    view.rerender(createTestTree(queryClient));

    await waitFor(() => expect(firstJob.setCurrentStep).toHaveBeenCalledOnce());
    expect(firstJob.setCurrentStep.mock.calls[0][0](2)).toBe(0);
    expect(scrollTo).toHaveBeenCalledTimes(3);
  });

  it('normalizes when the loaded job changes from non-rejected to rejected', async () => {
    const queryClient = new QueryClient();
    const setCurrentStep = vi.fn();
    let details = { ...createDetails('job-1', JobStatus.InReview, 3), setCurrentStep };
    mocks.useJobDetails.mockImplementation(() => details);

    const view = render(createTestTree(queryClient));
    expect(setCurrentStep).not.toHaveBeenCalled();

    details = { ...details, job: { ...details.job, status: JobStatus.Rejected } };
    view.rerender(createTestTree(queryClient));

    await waitFor(() => expect(setCurrentStep).toHaveBeenCalledOnce());
    expect(setCurrentStep.mock.calls[0][0](3)).toBe(0);
    expect(scrollTo).toHaveBeenCalledOnce();
  });

  it('preserves the current step for a non-rejected job', () => {
    const details = createDetails('job-1', JobStatus.InReview, 2);
    mocks.useJobDetails.mockReturnValue(details);

    render(createTestTree(new QueryClient()));

    expect(screen.getByText('Wizard step 2')).toBeInTheDocument();
    expect(details.setCurrentStep).not.toHaveBeenCalled();
  });

  it('keeps the existing admin redirect for rejected jobs', async () => {
    mocks.isAdmin = true;
    const details = createDetails('job-1', JobStatus.Rejected, 2);
    mocks.useJobDetails.mockReturnValue(details);

    render(createTestTree(new QueryClient()));

    expect(await screen.findByText('Completed report')).toBeInTheDocument();
    expect(details.setCurrentStep).not.toHaveBeenCalled();
    expect(mocks.markJobAsSeen).not.toHaveBeenCalled();
  });
});
