import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, render, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { JobReportSummaryViewModel } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { CompletedJobReport } from './CompletedJobReport';

const mocks = vi.hoisted(() => ({
  isAdmin: true,
  markJobAsSeen: vi.fn(),
  job: null as JobReportSummaryViewModel | null,
}));

const mutation = vi.hoisted(() => ({
  isPending: false,
  mutateAsync: vi.fn(),
}));

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useParams: () => ({ id: 'job-1' }),
    useLocation: () => ({ state: { from: '/app' } }),
    useNavigate: () => vi.fn(),
  };
});

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['jobs', id],
  getGetApiJobsQueryKey: () => ['jobs'],
  usePostApiJobsIdStatus: () => mutation,
}));

vi.mock('../../../providers/permissions/usePermissions', () => ({
  useIsAdmin: () => mocks.isAdmin,
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
    job: mocks.job,
    form: { reportNumber: '0001' },
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
  COMPLETED_JOB_VIEW_TYPE: 'Completed',
  markJobAsSeen: mocks.markJobAsSeen,
}));

vi.mock('../statusLabels', () => ({
  formatJobStatus: (status: string) => status,
}));

vi.mock('../utils/completedJobFormatters', () => ({
  formatReportNumber: () => 'SAG-0001',
  formatWorkKind: () => 'Service',
  formatInstallationTypeNames: () => '',
  formatClosureFlags: () => '',
}));

vi.mock('../utils/downloadJobReportPdf', () => ({
  createJobReportPdfPreview: vi.fn(),
  downloadJobReportPdf: vi.fn(),
}));

vi.mock('../../../components/ErrorState', () => ({ ErrorState: () => null }));
vi.mock('../../../components/StatusBanner', () => ({ StatusBanner: () => null }));
vi.mock('../../../components/forms/CollapsibleSection', () => ({ CollapsibleSection: () => null }));
vi.mock('../../../components/forms/NavigationGuard', () => ({ NavigationGuard: () => null }));
vi.mock('../components/ConfirmActionDialog', () => ({ ConfirmActionDialog: () => null }));
vi.mock('../components/CompletedJobEditForm', () => ({ CompletedJobEditForm: () => null }));
vi.mock('../components/DetailGrid', () => ({ DetailGrid: () => null }));
vi.mock('../components/AssignedUsers', () => ({ AssignedUsers: () => null }));
vi.mock('../components/LinkedJobs', () => ({ LinkedJobs: () => null }));
vi.mock('../components/WorksheetDetailList', () => ({ WorksheetDetailList: () => null }));
vi.mock('../components/JobHistoryDrawer', () => ({ JobHistoryDrawer: () => null }));
vi.mock('../components/ControlPointOverview', () => ({
  getSelectedControlPoints: () => [],
  getIrrelevantCategories: () => [],
  ControlPointOverview: () => null,
}));

function createJob(status: JobStatus): JobReportSummaryViewModel {
  return {
    id: 'job-1',
    organizationId: 'org-1',
    reportNumber: '0001',
    status,
    customerId: 'customer-1',
    customerSnapshot: {
      name: 'Kunde A/S',
      address: 'Testvej 1',
      contactPerson: 'Test Person',
      phone: '12345678',
      email: 'test@example.com',
    },
    destinationAddress: 'Testvej 2',
    destinationZipCode: '8000',
    destinationCity: 'Aarhus',
    work: {
      workKind: null,
      installationTypes: [],
      closureFlags: [],
      remarks: null,
    },
    observations: {
      taskDescription: 'Testopgave',
      customerObservations: null,
      technicalObservations: null,
    },
    links: [],
    assignedUsers: [],
    worksheets: [],
    totalHours: 0,
    totalOutlay: null,
    softDeleted: false,
    jobType: 'KLS',
    rejectionNote: status === JobStatus.Rejected ? 'Ret sagen' : null,
  } as JobReportSummaryViewModel;
}

function renderReport() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/app/completed/job-1']}>
        <CompletedJobReport />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('CompletedJobReport seen-state handling', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.isAdmin = true;
    mocks.job = createJob(JobStatus.Rejected);
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0);
      return 0;
    });
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('marks a rejected report as normally seen when an admin opens it', async () => {
    renderReport();

    await waitFor(() => {
      expect(mocks.markJobAsSeen).toHaveBeenCalledOnce();
    });
    expect(mocks.markJobAsSeen).toHaveBeenCalledWith('job-1', expect.any(QueryClient));
  });

  it('marks a rejected report as normally seen for an ordinary user', async () => {
    mocks.isAdmin = false;
    renderReport();

    await waitFor(() => {
      expect(mocks.markJobAsSeen).toHaveBeenCalledOnce();
    });
    expect(mocks.markJobAsSeen).toHaveBeenCalledWith('job-1', expect.any(QueryClient));
  });

  it('still marks a non-rejected report as seen for an admin', async () => {
    mocks.job = createJob(JobStatus.InReview);
    renderReport();

    await waitFor(() => {
      expect(mocks.markJobAsSeen).toHaveBeenCalledOnce();
    });
    expect(mocks.markJobAsSeen).toHaveBeenCalledWith('job-1', expect.any(QueryClient));
  });

  it('marks an approved report with the completed view type for an ordinary user', async () => {
    mocks.isAdmin = false;
    mocks.job = createJob(JobStatus.Approved);
    renderReport();

    await waitFor(() => {
      expect(mocks.markJobAsSeen).toHaveBeenCalledOnce();
    });
    expect(mocks.markJobAsSeen).toHaveBeenCalledWith(
      'job-1',
      expect.any(QueryClient),
      'Completed',
    );
  });

});
