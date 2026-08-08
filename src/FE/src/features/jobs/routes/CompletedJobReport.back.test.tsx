import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { CompletedJobReport } from './CompletedJobReport';

const mocks = vi.hoisted(() => ({
  details: {
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
    job: {
      id: 'job-1',
      status: JobStatus.Approved,
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
vi.mock('../../../providers/permissions/usePermissions', () => ({ useIsAdmin: () => false }));
vi.mock('../../../providers/useAuth', () => ({ useAuth: () => ({ user: { id: 'user-1' } }) }));
vi.mock('../../../hooks/useMediaQuery', () => ({ useMediaQuery: () => false }));
vi.mock('../../../hooks/useScrollRestore', () => ({ useScrollRestore: vi.fn() }));
vi.mock('../../../components/forms/NavigationGuard', () => ({ NavigationGuard: () => null }));
vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['job', id],
  getGetApiJobsQueryKey: () => ['jobs'],
  usePostApiJobsIdStatus: () => ({ isPending: false, mutateAsync: vi.fn() }),
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
vi.mock('../components/ConfirmActionDialog', () => ({ ConfirmActionDialog: () => null }));
vi.mock('../components/CompletedJobEditForm', () => ({ CompletedJobEditForm: () => null }));
vi.mock('../components/DetailGrid', () => ({ DetailGrid: () => null }));
vi.mock('../components/AssignedUsers', () => ({ AssignedUsers: () => null }));
vi.mock('../components/LinkedJobs', () => ({ LinkedJobs: () => null }));
vi.mock('../components/WorksheetDetailList', () => ({ WorksheetDetailList: () => null }));
vi.mock('../../../components/forms/CollapsibleSection', () => ({ CollapsibleSection: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
vi.mock('../../../components/StatusBanner', () => ({ StatusBanner: ({ children }: { children: React.ReactNode }) => <>{children}</> }));

function renderReport(from?: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const state = from ? { from } : undefined;

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[{ pathname: '/app/completed/job-1', state }]}>
        <Routes>
          <Route path="/app/completed/:id" element={<CompletedJobReport />} />
          <Route path="/app" element={<h1>Forside</h1>} />
          <Route path="/app/timer" element={<h1>Timer</h1>} />
          <Route path="/app/auditor" element={<h1>Auditor</h1>} />
          <Route path="/app/customers/:id" element={<h1>Kunde</h1>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
    configurable: true,
    value: vi.fn(),
  });
});

afterEach(cleanup);

describe('CompletedJobReport back navigation', () => {
  it.each([
    ['/app/timer', 'Timer'],
    ['/app/auditor', 'Auditor'],
    ['/app/customers/customer-1', 'Kunde'],
  ])('returns to the explicit source %s', async (from, heading) => {
    renderReport(from);

    fireEvent.click(screen.getByRole('button', { name: 'Tilbage til afsluttede sager' }));

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
  });

  it('falls back to app home for a direct entry without source state', async () => {
    renderReport();

    fireEvent.click(screen.getByRole('button', { name: 'Tilbage til afsluttede sager' }));

    expect(await screen.findByRole('heading', { name: 'Forside' })).toBeInTheDocument();
  });
});
