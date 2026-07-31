import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SimpleJobCreate } from './SimpleJobCreate';
import type { WorksheetDraft } from '../components/worksheetUtils';

const { navigateMock } = vi.hoisted(() => ({ navigateMock: vi.fn() }));

const { jobCreateState } = vi.hoisted(() => ({
  jobCreateState: { onCreated: null as ((jobId: string) => void) | null },
}));

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigateMock };
});

vi.mock('../../../lib/axios', () => ({
  apiClient: { get: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }) },
}));

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsQueryKey: () => ['jobs'],
  usePostApiJobsIdStatus: () => ({
    mutate: vi.fn((_args: unknown, opts: { onSuccess?: () => void } | undefined) => opts?.onSuccess?.()),
  }),
}));

vi.mock('../hooks/useJobCreate', () => ({
  useJobCreate: (onCreated: (jobId: string) => void) => {
    jobCreateState.onCreated = onCreated;
    return {
      form: {
        customerId: null,
        customerSnapshot: null,
        editSnapshot: false,
        createCustomer: false,
        reportNumber: '',
        destinationAddress: '',
        destinationZipCode: '',
        destinationCity: '',
        taskDescription: '',
        customerObservations: '',
        technicalObservations: '',
        work: {
          categoryIds: [],
          workKind: '',
          customWorkKind: '',
          controlPointSelections: {},
          irrelevantCategoryIds: [],
          closureFlags: [],
        },
        jobType: 'Diverse',
        timesheets: [],
      },
      linkedJobIds: [],
      assignedUserIds: [],
      assignableUsers: [],
      isSaving: false,
      canSave: true,
      linksStatus: 'idle',
      assignmentStatus: 'idle',
      referenceData: null,
      isLoadingReferenceData: false,
      isLoadingUsers: false,
      selectCustomer: vi.fn(),
      createNewCustomer: vi.fn(),
      updateSnapshotField: vi.fn(),
      updateEditSnapshot: vi.fn(),
      updateCreateCustomer: vi.fn(),
      updateDestinationAddress: vi.fn(),
      updateDestinationZipCode: vi.fn(),
      updateDestinationCity: vi.fn(),
      updateJobType: vi.fn(),
      updateTimesheets: vi.fn(),
      updateTaskDescription: vi.fn(),
      updateCustomerObservations: vi.fn(),
      updateTechnicalObservations: vi.fn(),
      updateLinkedJobs: vi.fn(),
      updateAssignedUsers: vi.fn(),
      updateWorkCategories: vi.fn(),
      updateWorkKind: vi.fn(),
      updateCustomWorkKind: vi.fn(),
      fieldErrors: {},
      save: () => {
        jobCreateState.onCreated?.('job-1');
      },
      reset: vi.fn(),
    };
  },
}));

vi.mock('../../../components/forms/NavigationGuard', () => ({
  NavigationGuard: () => null,
}));

vi.mock('../components/steps/CreateOverviewStep', () => ({
  CreateOverviewStep: () => <div>overview-step</div>,
}));

vi.mock('../components/steps/JobWorksheetsStep', () => ({
  JobWorksheetsStep: ({ onChange }: { onChange: (drafts: WorksheetDraft[]) => void }) => (
    <button
      type="button"
      onClick={() =>
        onChange([{ userId: 'user-1', workDate: '2026-07-31', hours: 8, sleptOnJob: false }])
      }
    >
      add-worksheet
    </button>
  ),
}));

afterEach(() => cleanup());

function renderSimpleJobCreate() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/app/job/simple/new']}>
        <Routes>
          <Route path="/app/job/simple/new" element={<SimpleJobCreate />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('SimpleJobCreate', () => {
  it('navigates to the completed/review view when "Til sagen" is pressed after creation', () => {
    renderSimpleJobCreate();

    fireEvent.click(screen.getByRole('button', { name: 'add-worksheet' }));
    fireEvent.click(screen.getByRole('button', { name: 'Opret job' }));

    const tilSagen = screen.getByRole('button', { name: 'Til sagen' });
    expect(tilSagen).toBeInTheDocument();

    fireEvent.click(tilSagen);

    expect(navigateMock).toHaveBeenCalledWith(
      '/app/completed/job-1',
      expect.objectContaining({ replace: true }),
    );
    expect(navigateMock).not.toHaveBeenCalledWith(
      '/app/job/job-1',
      expect.anything(),
    );
  });
});
