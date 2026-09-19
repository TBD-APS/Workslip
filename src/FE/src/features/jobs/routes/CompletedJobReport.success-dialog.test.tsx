import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CompletedJobReport } from './CompletedJobReport';

const mocks = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  details: {
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
    job: {
      id: 'job-1',
      status: 'InReview',
      jobType: 'KLS',
      destinationAddress: 'Testvej 1',
      work: { installationTypes: [], remarks: null },
      worksheets: [],
      totalOutlay: 0,
      totalHours: 0,
      customerSnapshot: { name: 'Testkunde', address: null, contactPerson: null, phone: null, email: null },
      observations: { taskDescription: null, customerObservations: null, technicalObservations: null },
      assignedUsers: [],
      links: [],
    },
    form: { reportNumber: '123' },
    referenceData: {},
    hasUnsavedChanges: false,
    saveAllChanges: vi.fn(),
    discardChanges: vi.fn(),
    saveStatus: 'idle',
  },
}));

vi.mock('../hooks/useJobDetails', () => ({
  useJobDetailsState: () => mocks.details,
}));
vi.mock('../../../providers/permissions/usePermissions', () => ({ useIsAdmin: () => true }));
vi.mock('../../../providers/useAuth', () => ({ useAuth: () => ({ user: { id: 'user-1' } }) }));
vi.mock('../../../hooks/useMediaQuery', () => ({ useMediaQuery: () => false }));
vi.mock('../../../hooks/useScrollRestore', () => ({ useScrollRestore: vi.fn() }));
vi.mock('../../../components/forms/NavigationGuard', () => ({ NavigationGuard: () => null }));
vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['job', id],
  getGetApiJobsQueryKey: () => ['jobs'],
  usePostApiJobsIdStatus: () => ({ isPending: false, mutateAsync: mocks.mutateAsync }),
}));
vi.mock('../utils/markJobSeen', () => ({
  COMPLETED_JOB_VIEW_TYPE: 'completed',
  markJobAsSeen: vi.fn(),
}));
vi.mock('../utils/downloadJobReportPdf', () => ({
  createJobReportPdfPreview: vi.fn(),
  downloadJobReportPdf: vi.fn(),
}));
vi.mock('../utils/completedJobFormatters', () => ({
  formatReportNumber: () => 'SAG-123',
  formatWorkKind: () => 'Test',
  formatInstallationTypeNames: () => 'Ingen',
  formatClosureFlags: () => 'Ingen',
}));
vi.mock('../components/steps/controlPointsValidation', () => ({ validateControlPoints: () => ({ valid: true }) }));
vi.mock('../components/ControlPointOverview', () => ({
  ControlPointOverview: () => null,
  getSelectedControlPoints: () => [],
  getIrrelevantCategories: () => [],
}));
vi.mock('../components/JobHistoryDrawer', () => ({ JobHistoryDrawer: () => null }));
vi.mock('../components/JobStatusDots', () => ({ JobStatusDots: () => null }));
vi.mock('../components/ConfirmActionDialog', () => ({
  // Stand-in for the confirm step: the success dialog is the surface under test.
  ConfirmActionDialog: ({ onConfirm }: { onConfirm: (note?: string) => void }) => (
    <button type="button" onClick={() => onConfirm('')}>Bekræft handling</button>
  ),
}));
vi.mock('../components/CompletedJobEditForm', () => ({ CompletedJobEditForm: () => null }));
vi.mock('../components/DetailGrid', () => ({ DetailGrid: () => null }));
vi.mock('../components/AssignedUsers', () => ({ AssignedUsers: () => null }));
vi.mock('../components/LinkedJobs', () => ({ LinkedJobs: () => null }));
vi.mock('../components/WorksheetDetailList', () => ({ WorksheetDetailList: () => null }));
vi.mock('../../../components/forms/CollapsibleSection', () => ({ CollapsibleSection: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
vi.mock('../../../components/StatusBanner', () => ({ StatusBanner: ({ children }: { children: React.ReactNode }) => <>{children}</> }));

function renderReport() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[{ pathname: '/app/completed/job-1' }]}>
        <Routes>
          <Route path="/app/completed/:id" element={<CompletedJobReport />} />
          <Route path="/app" element={<h1>Forside</h1>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function approveAndOpenSuccessDialog() {
  renderReport();

  fireEvent.click(screen.getByRole('button', { name: 'Godkend' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Bekræft handling' }));

  return screen.findByRole('dialog', { name: 'Sagen er godkendt' });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.mutateAsync.mockResolvedValue({ ...mocks.details.job, status: 'Approved' });
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
    configurable: true,
    value: vi.fn(),
  });
});

afterEach(cleanup);

describe('CompletedJobReport action success dialog', () => {
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
