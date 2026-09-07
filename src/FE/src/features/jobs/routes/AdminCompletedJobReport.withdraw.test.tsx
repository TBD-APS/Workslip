import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminCompletedJobReport } from './AdminCompletedJobReport';

const mocks = vi.hoisted(() => ({
  isAdmin: false,
  status: 'InReview',
  assignedUsers: [{ id: 'user-1', displayName: 'Bruger Et' }] as { id: string; displayName: string }[],
  mutateAsync: vi.fn(),
  notifySuccess: vi.fn(),
  notifyError: vi.fn(),
}));

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['job', id],
  getGetApiJobsQueryKey: () => ['jobs'],
  useGetApiJobsId: () => ({
    data: {
      id: 'job-1',
      status: mocks.status,
      jobType: 'KLS',
      destinationAddress: 'Testvej 1',
      customerSnapshot: { name: 'Testkunde', address: null },
      observations: { taskDescription: null, customerObservations: null, technicalObservations: null },
      assignedUsers: mocks.assignedUsers,
      work: { installationTypes: [], remarks: null },
      worksheets: [],
      totalOutlay: 0,
      totalHours: 0,
      links: [],
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useGetApiJobsIdHistory: () => ({ data: [] }),
  usePostApiJobsIdStatus: () => ({ isPending: false, mutateAsync: mocks.mutateAsync }),
}));
vi.mock('../../../providers/permissions/usePermissions', () => ({ useIsAdmin: () => mocks.isAdmin }));
vi.mock('../../../providers/useAuth', () => ({ useAuth: () => ({ user: { id: 'user-1' } }) }));
vi.mock('../../../lib/toast', () => ({ notify: { success: mocks.notifySuccess, error: mocks.notifyError, warning: vi.fn() } }));
vi.mock('../utils/markJobSeen', () => ({ COMPLETED_JOB_VIEW_TYPE: 'completed', markJobAsSeen: vi.fn() }));
vi.mock('../utils/downloadJobReportPdf', () => ({ createJobReportPdfPreview: vi.fn(), downloadJobReportPdf: vi.fn() }));
vi.mock('../utils/completedJobFormatters', () => ({
  formatClosureFlags: () => '',
  formatInstallationTypeNames: () => '',
  formatReportNumber: () => 'SAG-123',
  formatWorkKind: () => '',
}));
vi.mock('../components/JobConversationLauncher', () => ({ JobConversationLauncher: () => null }));
vi.mock('../components/JobStatusDots', () => ({ JobStatusDots: () => null }));
vi.mock('../components/DetailGrid', () => ({ DetailGrid: () => null }));
vi.mock('../components/LinkedJobs', () => ({ LinkedJobs: () => null }));
vi.mock('../components/WorksheetDetailList', () => ({ WorksheetDetailList: () => null }));
vi.mock('../components/ControlPointOverview', () => ({
  ControlPointOverview: () => null,
  getSelectedControlPoints: () => [],
  getIrrelevantCategories: () => [],
}));
vi.mock('../../images/JobImagesSection', () => ({ JobImagesSection: () => null }));

function EditorProbe() {
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? 'none';
  return <div>{`editor:${location.pathname}:from=${from}`}</div>;
}

function renderReport(state?: { from?: string; readOnly?: boolean }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[{ pathname: '/app/completed/job-1', state }]}>
        <Routes>
          <Route path="/app/completed/:id" element={<AdminCompletedJobReport />} />
          <Route path="/app/job/:id" element={<EditorProbe />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const withdrawButton = () => document.getElementById('job-report-withdraw-review');

describe('AdminCompletedJobReport withdraw from review', () => {
  afterEach(cleanup);

  beforeEach(() => {
    mocks.isAdmin = false;
    mocks.status = 'InReview';
    mocks.assignedUsers = [{ id: 'user-1', displayName: 'Bruger Et' }];
    mocks.mutateAsync.mockReset();
    mocks.notifySuccess.mockReset();
    mocks.notifyError.mockReset();
  });

  it('lets the assigned user withdraw a submitted job and lands in the editor', async () => {
    mocks.mutateAsync.mockResolvedValue({ id: 'job-1', status: 'Draft' });
    renderReport({ from: '/app/timer' });

    const button = withdrawButton();
    expect(button).not.toBeNull();
    fireEvent.click(button!);

    const confirm = document.getElementById('job-report-withdraw-review-confirm');
    expect(confirm).not.toBeNull();
    await act(async () => {
      fireEvent.click(confirm!);
    });

    expect(mocks.mutateAsync).toHaveBeenCalledWith({ id: 'job-1', data: { status: 'Draft', rejectionNote: null } });
    expect(await screen.findByText('editor:/app/job/job-1:from=/app/timer')).toBeInTheDocument();
    expect(mocks.notifySuccess).toHaveBeenCalledTimes(1);
  });

  it('offers withdraw to admins even when they are not assigned', () => {
    mocks.isAdmin = true;
    mocks.assignedUsers = [];
    renderReport();

    expect(withdrawButton()).not.toBeNull();
  });

  it('hides withdraw for a user who is not assigned to the job', () => {
    mocks.assignedUsers = [{ id: 'someone-else', displayName: 'Anden Bruger' }];
    renderReport();

    expect(withdrawButton()).toBeNull();
  });

  it('hides withdraw in read-only mode', () => {
    renderReport({ readOnly: true });

    expect(withdrawButton()).toBeNull();
  });

  it.each(['Draft', 'Approved', 'Rejected', 'Reopened'])('hides withdraw when the job is %s', (status) => {
    mocks.status = status;
    renderReport();

    expect(withdrawButton()).toBeNull();
  });

  it('keeps the job on the report and reports the failure when withdraw is rejected', async () => {
    mocks.mutateAsync.mockRejectedValue(new Error('409'));
    renderReport();

    fireEvent.click(withdrawButton()!);
    await act(async () => {
      fireEvent.click(document.getElementById('job-report-withdraw-review-confirm')!);
    });

    expect(mocks.notifyError).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(/editor:/)).not.toBeInTheDocument();
    expect(withdrawButton()).not.toBeNull();
  });
});
