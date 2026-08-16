import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CompletedJobReport } from './CompletedJobReport';

const mocks = vi.hoisted(() => ({
  confirmAction: null as string | null,
  details: {
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
    job: {
      id: 'job-locked',
      status: 'Approved',
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

vi.mock('../hooks/useJobDetails', () => ({ useJobDetailsState: () => mocks.details }));
vi.mock('../../../providers/permissions/usePermissions', () => ({ useIsAdmin: () => true }));
vi.mock('../../../providers/useAuth', () => ({ useAuth: () => ({ user: { id: 'admin-1' } }) }));
vi.mock('../../../hooks/useMediaQuery', () => ({ useMediaQuery: () => true }));
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
vi.mock('../components/ConfirmActionDialog', () => ({
  ConfirmActionDialog: ({ action }: { action: string }) => {
    mocks.confirmAction = action;
    return <div data-testid="confirm-action">{action}</div>;
  },
}));
vi.mock('../components/CompletedJobEditForm', () => ({ CompletedJobEditForm: () => <div>Redigeringsformular</div> }));
vi.mock('../components/DetailGrid', () => ({ DetailGrid: () => null }));
vi.mock('../components/AssignedUsers', () => ({ AssignedUsers: () => null }));
vi.mock('../components/LinkedJobs', () => ({ LinkedJobs: () => null }));
vi.mock('../components/WorksheetDetailList', () => ({ WorksheetDetailList: () => null }));
vi.mock('../../../components/forms/CollapsibleSection', () => ({ CollapsibleSection: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
vi.mock('../../../components/StatusBanner', () => ({ StatusBanner: ({ title, children }: { title: string; children: React.ReactNode }) => <div><strong>{title}</strong>{children}</div> }));

function renderReport() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/app/completed/job-locked']}>
        <Routes>
          <Route path="/app/completed/:id" element={<CompletedJobReport />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.confirmAction = null;
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
    configurable: true,
    value: vi.fn(),
  });
});

describe('CompletedJobReport immutable approved state', () => {
  it('removes direct edit and presents reopen as the guided action', () => {
    renderReport();

    expect(screen.queryByRole('button', { name: 'Rediger sag' })).not.toBeInTheDocument();
    expect(screen.getByText('Godkendt og låst')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Genåbn sag' }));

    expect(screen.getByTestId('confirm-action')).toHaveTextContent('reopen');
    expect(mocks.confirmAction).toBe('reopen');
  });
});
