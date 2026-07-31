import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { JobReportSummaryViewModel } from '../../../api/generated/models';
import { CompletedJobReport } from './CompletedJobReport';

const { navigateMock } = vi.hoisted(() => ({ navigateMock: vi.fn() }));

const state = vi.hoisted(() => {
  const job: JobReportSummaryViewModel = {
    id: 'job-1',
    organizationId: 'org-1',
    reportNumber: '123',
    status: 'InReview',
    customerId: 'cust-1',
    customerSnapshot: {
      name: 'ACME A/S',
      address: 'Vej 1',
      contactPerson: 'Bo',
      phone: '12345678',
      email: 'bo@acme.dk',
    },
    destinationAddress: 'Vej 2',
    destinationZipCode: '1000',
    destinationCity: 'København',
    work: {
      workKind: {
        id: 'wk-1',
        normalizedLabel: 'service',
        label: 'Service',
        requiresCustomWorkKind: false,
        sortOrder: 1,
        customWorkKind: null,
      },
      installationTypes: [],
      closureFlags: [],
      remarks: null,
    },
    observations: {
      taskDescription: 'Opgave',
      customerObservations: null,
      technicalObservations: null,
    },
    links: [],
    assignedUsers: [],
    worksheets: [],
    totalHours: 8,
    totalOutlay: null,
    softDeleted: false,
    jobType: 'KLS',
    rejectionNote: null,
  };
  return { job };
});

const mutation = vi.hoisted(() => ({
  isPending: false,
  mutateAsync: vi.fn(),
}));

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useNavigate: () => navigateMock,
    useParams: () => ({ id: 'job-1' }),
    useLocation: () => ({ state: { from: '/app' } }),
  };
});

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['jobs', id],
  getGetApiJobsQueryKey: () => ['jobs'],
  usePostApiJobsIdStatus: () => mutation,
}));

vi.mock('../../../lib/toast', () => ({
  notify: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../../../providers/permissions/usePermissions', () => ({
  useIsAdmin: () => true,
}));

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({ user: { id: 'user-1' } }),
}));

vi.mock('../../../hooks/useMediaQuery', () => ({
  useMediaQuery: () => true,
}));

vi.mock('../../../hooks/useScrollRestore', () => ({
  useScrollRestore: vi.fn(),
}));

vi.mock('../hooks/useJobDetails', () => ({
  useJobDetailsState: () => ({
    job: state.job,
    form: { reportNumber: '123' },
    referenceData: null,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
    discardChanges: vi.fn(),
    saveAllChanges: vi.fn(),
    hasUnsavedChanges: false,
    saveStatus: 'idle',
  }),
}));

vi.mock('../utils/markJobSeen', () => ({
  markJobAsSeen: vi.fn(),
}));

vi.mock('../components/ConfirmActionDialog', () => ({
  ConfirmActionDialog: ({
    action,
    onConfirm,
  }: {
    action: 'approve' | 'reject' | 'undo-reject';
    onConfirm: (note?: string) => void;
  }) => (
    <button type="button" onClick={() => onConfirm()}>
      {action === 'approve'
        ? 'Bekræft godkendelse'
        : action === 'reject'
          ? 'Bekræft afvisning'
          : 'Bekræft fortrydelse'}
    </button>
  ),
}));

vi.mock('../components/JobHistoryDrawer', () => ({ JobHistoryDrawer: () => null }));
vi.mock('../components/CompletedJobEditForm', () => ({ CompletedJobEditForm: () => null }));
vi.mock('../components/DetailGrid', () => ({ DetailGrid: () => null }));
vi.mock('../components/AssignedUsers', () => ({ AssignedUsers: () => null }));
vi.mock('../components/LinkedJobs', () => ({ LinkedJobs: () => null }));
vi.mock('../components/WorksheetDetailList', () => ({ WorksheetDetailList: () => null }));
vi.mock('../components/ControlPointOverview', () => ({
  getSelectedControlPoints: () => [],
  getIrrelevantCategories: () => [],
  ControlPointOverview: () => null,
}));
vi.mock('../../../components/forms/CollapsibleSection', () => ({ CollapsibleSection: () => null }));
vi.mock('../../../components/forms/NavigationGuard', () => ({ NavigationGuard: () => null }));
vi.mock('../../../components/StatusBanner', () => ({ StatusBanner: () => null }));
vi.mock('../../../components/ErrorState', () => ({ ErrorState: () => null }));

function renderCompletedJobReport() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/app/completed/job-1']}>
        <Routes>
          <Route path="/app/completed/:id" element={<CompletedJobReport />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function approveJob() {
  fireEvent.click(screen.getByRole('button', { name: 'Godkend' }));
  fireEvent.click(screen.getByRole('button', { name: 'Bekræft godkendelse' }));
  await screen.findByRole('heading', { name: 'Sagen er godkendt' });
}

async function rejectJob() {
  fireEvent.click(screen.getByRole('button', { name: 'Afvis' }));
  fireEvent.click(screen.getByRole('button', { name: 'Bekræft afvisning' }));
  await screen.findByRole('heading', { name: 'Sagen er afvist' });
}

describe('CompletedJobReport post-approval navigation', () => {
  beforeEach(() => {
    mutation.mutateAsync.mockResolvedValue(state.job);
  });

  afterEach(() => cleanup());

  it('shows the navigation modal after approving without navigating yet', async () => {
    renderCompletedJobReport();

    await approveJob();

    expect(screen.getByRole('button', { name: 'Til sagslisten' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Til sagen' })).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('navigates to the job list from the modal after approving', async () => {
    renderCompletedJobReport();

    await approveJob();
    fireEvent.click(screen.getByRole('button', { name: 'Til sagslisten' }));

    expect(navigateMock).toHaveBeenCalledWith('/app', expect.objectContaining({ replace: true }));
  });

  it('navigates to the case from the modal after approving', async () => {
    renderCompletedJobReport();

    await approveJob();
    fireEvent.click(screen.getByRole('button', { name: 'Til sagen' }));

    expect(navigateMock).toHaveBeenCalledWith(
      '/app/completed/job-1',
      expect.objectContaining({ replace: true }),
    );
  });

  it('shows the navigation modal after rejecting', async () => {
    renderCompletedJobReport();

    await rejectJob();

    expect(screen.getByRole('button', { name: 'Til sagslisten' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Til sagen' })).toBeInTheDocument();
  });
});
