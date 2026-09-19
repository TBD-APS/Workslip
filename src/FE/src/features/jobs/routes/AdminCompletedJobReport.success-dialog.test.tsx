import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminCompletedJobReport } from './AdminCompletedJobReport';

const mocks = vi.hoisted(() => ({ mutateAsync: vi.fn() }));

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['job', id],
  getGetApiJobsQueryKey: () => ['jobs'],
  useGetApiJobsId: () => ({
    data: {
      id: 'job-1',
      status: 'InReview',
      jobType: 'KLS',
      destinationAddress: 'Testvej 1',
      customerSnapshot: { name: 'Testkunde', address: null },
      observations: { taskDescription: null, customerObservations: null, technicalObservations: null },
      assignedUsers: [],
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
vi.mock('../../../providers/permissions/usePermissions', () => ({ useIsAdmin: () => true }));
vi.mock('../../../providers/useAuth', () => ({ useAuth: () => ({ user: { id: 'user-1' } }) }));
vi.mock('../utils/markJobSeen', () => ({ COMPLETED_JOB_VIEW_TYPE: 'completed', markJobAsSeen: vi.fn() }));
vi.mock('../utils/downloadJobReportPdf', () => ({ createJobReportPdfPreview: vi.fn(), downloadJobReportPdf: vi.fn() }));
vi.mock('../utils/completedJobFormatters', () => ({
  formatClosureFlags: () => '',
  formatInstallationTypeNames: () => '',
  formatReportNumber: () => 'SAG-123',
  formatWorkKind: () => '',
}));
vi.mock('../components/ConfirmActionDialog', () => ({
  // Stand-in for the confirm step: the success dialog is the surface under test.
  ConfirmActionDialog: ({ onConfirm }: { onConfirm: (note?: string) => void }) => (
    <button type="button" onClick={() => onConfirm('')}>Bekræft handling</button>
  ),
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

async function approveAndOpenSuccessDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/app/completed/job-1']}>
        <Routes>
          <Route path="/app/completed/:id" element={<AdminCompletedJobReport />} />
          <Route path="/app" element={<h1>Forside</h1>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );

  fireEvent.click(screen.getByRole('button', { name: 'Godkend' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Bekræft handling' }));

  return screen.findByRole('dialog', { name: 'Sagen er godkendt' });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.mutateAsync.mockResolvedValue({ id: 'job-1', status: 'Approved' });
});

afterEach(cleanup);

describe('AdminCompletedJobReport action success dialog', () => {
  it('does not navigate when Escape is pressed', async () => {
    const dialog = await approveAndOpenSuccessDialog();

    fireEvent.keyDown(document, { key: 'Escape' });

    // Both buttons are route changes, so Escape has no dismiss-only meaning here.
    // Leaving the dialog must take an explicit press.
    expect(dialog).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Forside' })).not.toBeInTheDocument();
  });

  it('goes to the case list on an explicit press', async () => {
    await approveAndOpenSuccessDialog();

    fireEvent.click(screen.getByRole('button', { name: 'Til sagslisten' }));

    expect(await screen.findByRole('heading', { name: 'Forside' })).toBeInTheDocument();
  });

  it('keeps the keyboard inside the dialog', async () => {
    const dialog = await approveAndOpenSuccessDialog();

    await waitFor(() => expect(screen.getByRole('button', { name: 'Til sagen' })).toHaveFocus());
    expect(dialog).toHaveAttribute('aria-modal', 'true');

    fireEvent.keyDown(document, { key: 'Tab' });

    expect(dialog.contains(document.activeElement)).toBe(true);
  });
});
