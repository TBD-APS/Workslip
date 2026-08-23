import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AdminCompletedJobReport } from './AdminCompletedJobReport';

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['job', id],
  getGetApiJobsQueryKey: () => ['jobs'],
  useGetApiJobsId: () => ({
    data: {
      id: 'job-1',
      status: 'Approved',
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
  usePostApiJobsIdStatus: () => ({ isPending: false, mutateAsync: vi.fn() }),
}));
vi.mock('../../../providers/permissions/usePermissions', () => ({ useIsAdmin: () => false }));
vi.mock('../../../providers/useAuth', () => ({ useAuth: () => ({ user: { id: 'user-1' } }) }));
vi.mock('../utils/markJobSeen', () => ({ COMPLETED_JOB_VIEW_TYPE: 'completed', markJobAsSeen: vi.fn() }));
vi.mock('../utils/downloadJobReportPdf', () => ({ createJobReportPdfPreview: vi.fn(), downloadJobReportPdf: vi.fn() }));
vi.mock('../utils/completedJobFormatters', () => ({
  formatClosureFlags: () => '',
  formatInstallationTypeNames: () => '',
  formatReportNumber: () => 'SAG-123',
  formatWorkKind: () => '',
}));
vi.mock('../components/ConfirmActionDialog', () => ({ ConfirmActionDialog: () => null }));
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

describe('AdminCompletedJobReport document flow link', () => {
  it('opens the existing Docs route from the case-file note', () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/app/completed/job-1']}>
          <Routes>
            <Route path="/app/completed/:id" element={<AdminCompletedJobReport />} />
            <Route path="/app/docs" element={<h1>Docs</h1>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const link = screen.getByRole('link', { name: 'Øvrige sagsfiler bevares i Workslips eksisterende dokumentationsflow.' });
    expect(link).toHaveAttribute('id', 'job-report-open-docs');
    expect(link).toHaveAttribute('href', '/app/docs');

    fireEvent.click(link);

    expect(screen.getByRole('heading', { name: 'Docs' })).toBeInTheDocument();
  });
});
