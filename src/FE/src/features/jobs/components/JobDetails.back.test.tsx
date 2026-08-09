import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JobStatus } from '../../../api/generated/models';
import type { useJobDetails } from '../hooks/useJobDetails';
import { JobDetailsPage } from './JobDetails';

vi.mock('../../../api/generated/jobs/jobs', () => ({
  useDeleteApiJobsId: () => ({ mutate: vi.fn() }),
  getGetApiJobsQueryKey: () => ['jobs'],
}));

vi.mock('../../../providers/permissions', () => ({
  useCan: () => false,
  useIsAdmin: () => false,
}));

vi.mock('../utils', () => ({
  isValidJobForm: () => true,
  isValidWork: () => true,
}));

vi.mock('./steps/controlPointsValidation', () => ({
  validateControlPoints: () => ({ valid: true }),
}));

vi.mock('./steps/JobOverviewStep', () => ({
  JobOverviewStep: () => <div>overview-step</div>,
}));

vi.mock('./steps/WorkCategoryStep', () => ({
  WorkCategoryStep: () => <div>work-step</div>,
}));

vi.mock('./steps/ControlPointsStep', () => ({
  ControlPointsStep: () => <div>control-points-step</div>,
}));

vi.mock('./steps/JobWorksheetsStep', () => ({
  JobWorksheetsStep: () => <div>worksheets-step</div>,
}));

vi.mock('./steps/JobCompletionStep', () => ({
  JobCompletionStep: () => <div>completion-step</div>,
}));

vi.mock('./steps/JobAttestationStep', () => ({
  JobAttestationStep: () => <div>attestation-step</div>,
}));

vi.mock('./steps/JobStepNavigation', () => ({
  StepNavigation: () => <div>step-navigation</div>,
}));

vi.mock('./steps/JobStepBar', () => ({
  JobStepBar: () => <div>job-step-bar</div>,
}));

vi.mock('./JobHistoryDrawer', () => ({
  JobHistoryDrawer: () => null,
}));

vi.mock('../../../components/common/ConfirmDeleteDialog', () => ({
  ConfirmDeleteDialog: () => null,
}));

vi.mock('../../../components/common/DeleteButton', () => ({
  DeleteButton: () => null,
}));

function createDetailsStub() {
  return {
    job: { id: 'job-1', reportNumber: '1234', jobType: 'KLS', status: JobStatus.Draft },
    form: { work: { closureFlags: [] } },
    referenceData: {},
    currentStep: 0,
    setCurrentStep: vi.fn(),
    isLoading: false,
    isError: false,
    isSubmittingJob: false,
    saveStatus: 'idle' as const,
    assignmentStatus: 'idle' as const,
    linksStatus: 'idle' as const,
    hasUnsavedChanges: true,
    reportNumberReadOnly: true,
    worksheets: [],
    isLoadingUsers: false,
    isLoadingReferenceData: false,
    isSavingWorksheet: false,
    isDeletingWorksheet: false,
    isAdmin: false,
    saveCurrentStep: vi.fn(),
    saveAllChanges: vi.fn(),
    discardChanges: vi.fn(),
    navigateToStep: vi.fn(),
    flushSave: vi.fn(),
    upsertWorksheet: vi.fn(),
    deleteWorksheet: vi.fn(),
    assignableUsers: [],
    assignedUserIds: [],
    linkableJobs: [],
    linkedJobIds: [],
    updateWorkCategories: vi.fn(),
    updateWorkKind: vi.fn(),
    updateCustomWorkKind: vi.fn(),
    toggleControlPoint: vi.fn(),
    toggleCategoryIrrelevant: vi.fn(),
    updateClosureFlags: vi.fn(),
    selectCustomer: vi.fn(),
    updateSnapshotField: vi.fn(),
    updateEditSnapshot: vi.fn(),
    updateCreateCustomer: vi.fn(),
    updateDestinationAddress: vi.fn(),
    updateDestinationZipCode: vi.fn(),
    updateDestinationCity: vi.fn(),
    updateTaskDescription: vi.fn(),
    updateCustomerObservations: vi.fn(),
    updateTechnicalObservations: vi.fn(),
    updateAssignedUsers: vi.fn(),
    updateLinkedJobs: vi.fn(),
    submitJob: vi.fn(),
    submitJobFieldErrors: [],
    saveCurrentStepAndSetCurrentStep: vi.fn(),
  } as unknown as ReturnType<typeof useJobDetails>;
}

function renderPage() {
  const details = createDetailsStub();
  const onBack = vi.fn();
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      {
        path: '/app/job/:id',
        element: (
          <JobDetailsPage
            details={details}
            onBack={onBack}
            onDone={vi.fn()}
            onGoToReport={vi.fn()}
          />
        ),
      },
    ],
    { initialEntries: ['/app/job/job-1'] },
  );
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { details, onBack };
}

describe('JobDetailsPage back navigation', () => {
  afterEach(cleanup);

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('does not save the current step when navigating back', () => {
    const { details, onBack } = renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Tilbage' }));

    expect(onBack).toHaveBeenCalledTimes(1);
    expect(details.saveCurrentStep).not.toHaveBeenCalled();
    expect(details.saveAllChanges).not.toHaveBeenCalled();
    expect(details.flushSave).not.toHaveBeenCalled();
  });
});
