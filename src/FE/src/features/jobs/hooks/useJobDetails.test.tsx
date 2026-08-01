import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { JobReportSummaryViewModel, ReferenceDataResponse } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models';
import { useJobDetailsState } from './useJobDetails';

const testState = vi.hoisted(() => ({
  job: undefined as JobReportSummaryViewModel | undefined,
  referenceData: undefined as ReferenceDataResponse | undefined,
  user: { id: 'user-1' },
  sameForm: true,
  sameFormWithoutWork: true,
  jobFormValid: true,
  workValid: true,
  controlPointsValid: true,
  patchOnSuccess: undefined as ((data: JobReportSummaryViewModel) => void) | undefined,
}));

const mutation = vi.hoisted(() => ({
  error: null,
  isPending: false,
  mutate: vi.fn(),
  mutateAsync: vi.fn(),
}));

const notify = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
}));

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['/api/jobs', id],
  getGetApiJobsQueryKey: () => ['/api/jobs'],
  useDeleteApiJobsIdLinks: () => mutation,
  useGetApiJobsId: () => ({
    data: testState.job,
    isError: false,
    isLoading: false,
    refetch: vi.fn(),
  }),
  usePatchApiJobsId: (options: {
    mutation: {
      onError: (error: unknown) => void;
      onSuccess: (data: JobReportSummaryViewModel) => void;
    };
  }) => {
    testState.patchOnSuccess = options.mutation.onSuccess;
    return {
      ...mutation,
      mutateAsync: async (...args: unknown[]) => {
        try {
          const data = await mutation.mutateAsync(...args) as JobReportSummaryViewModel;
          options.mutation.onSuccess(data);
          return data;
        } catch (error) {
          options.mutation.onError(error);
          throw error;
        }
      },
    };
  },
  usePostApiJobsIdAssign: () => mutation,
  usePostApiJobsIdLinks: () => mutation,
  usePostApiJobsIdStatus: () => mutation,
}));

vi.mock('../../../api/generated/worksheet/worksheet', () => ({
  useDeleteApiWorksheetsWorksheetIdJobsJobId: () => mutation,
  usePostApiWorksheetsJobsJobId: () => mutation,
}));

vi.mock('../../../api/generated/users/users', () => ({
  useGetApiUsers: () => ({ data: { users: [] }, isLoading: false }),
}));

vi.mock('../../../api/generated/reference-data/reference-data', () => ({
  useGetApiReferenceData: () => ({
    data: testState.referenceData,
    isLoading: !testState.referenceData,
  }),
}));

vi.mock('../../../lib/axios', () => ({
  apiClient: { get: vi.fn().mockResolvedValue({ items: [] }) },
}));

vi.mock('../../../lib/toast', () => ({
  notify,
}));

vi.mock('../../../providers/permissions', () => ({
  canReceiveJobAssignment: () => true,
  useIsAdmin: () => false,
}));

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({ user: testState.user }),
}));

vi.mock('../utils', () => {
  const form = {
    createCustomer: false,
    customerId: null,
    customerObservations: '',
    customerSnapshot: null,
    destinationAddress: '',
    destinationCity: '',
    destinationZipCode: '',
    editSnapshot: false,
    jobType: 'KLS',
    reportNumber: '',
    taskDescription: '',
    technicalObservations: '',
    timesheets: [],
    work: {
      categoryIds: [],
      closureFlags: [],
      controlPointSelections: {},
      customWorkKind: '',
      irrelevantCategoryIds: [],
      workKind: '',
    },
  };

  return {
    emptyForm: form,
    getLinkableJobs: () => [],
    getWorkValidationMessage: () => null,
    isValidJobForm: () => testState.jobFormValid,
    isValidWork: () => testState.workValid,
    sameForm: () => testState.sameForm,
    sameFormWithoutWork: () => testState.sameFormWithoutWork,
    toForm: () => form,
    toUpdateRequest: vi.fn(),
  };
});

vi.mock('../components/steps/controlPointsValidation', () => ({
  validateControlPoints: () => ({
    valid: testState.controlPointsValid,
    error: testState.controlPointsValid ? undefined : 'Kontrolpunkter mangler',
  }),
}));

const referenceData = {
  closureFlags: [],
  installationTypes: [],
  workKinds: [],
} as ReferenceDataResponse;

function createAssignedJob(status: JobStatus): JobReportSummaryViewModel {
  return {
    assignedUsers: [{ id: 'user-1', displayName: 'User' }],
    customerId: null,
    customerSnapshot: {
      address: null,
      contactPerson: null,
      email: null,
      name: null,
      phone: null,
    },
    destinationAddress: null,
    destinationCity: null,
    destinationZipCode: null,
    id: 'job-1',
    jobType: 'KLS',
    links: [],
    observations: {
      customerObservations: null,
      taskDescription: null,
      technicalObservations: null,
    },
    organizationId: 'organization-1',
    rejectionNote: status === JobStatus.Rejected ? 'Please correct the job' : null,
    reportNumber: '1',
    softDeleted: false,
    status,
    totalHours: 1,
    totalOutlay: 0,
    work: {
      closureFlags: [],
      installationTypes: [],
      remarks: null,
      workKind: null,
    },
    worksheets: [{
      createdAt: '2026-07-31T00:00:00Z',
      hoursWorked: 1,
      id: 'worksheet-1',
      jobId: 'job-1',
      organizationId: 'organization-1',
      sleptOnJob: false,
      updatedAt: '2026-07-31T00:00:00Z',
      userDisplayName: 'User',
      userId: 'user-1',
      workDate: '2026-07-31',
    }],
  };
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

describe('useJobDetailsState', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    testState.job = undefined;
    testState.referenceData = undefined;
    testState.sameForm = true;
    testState.sameFormWithoutWork = true;
    testState.jobFormValid = true;
    testState.workValid = true;
    testState.controlPointsValid = true;
    testState.patchOnSuccess = undefined;
    mutation.mutateAsync.mockResolvedValue(createAssignedJob(JobStatus.Draft));
  });

  it('does not redirect a rejected assigned job when reference data resolves later', async () => {
    testState.job = createAssignedJob(JobStatus.Rejected);
    const { result, rerender } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    expect(result.current.currentStep).toBe(0);

    await act(async () => {
      testState.referenceData = referenceData;
      rerender();
    });

    expect(result.current.currentStep).toBe(0);
  });

  it('does not redirect when a rejected assigned job resolves after reference data', async () => {
    testState.referenceData = referenceData;
    const { result, rerender } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    await act(async () => {
      testState.job = createAssignedJob(JobStatus.Rejected);
      rerender();
    });

    expect(result.current.currentStep).toBe(0);
  });

  it('retains the worksheet shortcut for an eligible non-rejected job', async () => {
    testState.job = createAssignedJob(JobStatus.InReview);
    const { result, rerender } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    await act(async () => {
      testState.referenceData = referenceData;
      rerender();
    });

    await waitFor(() => expect(result.current.currentStep).toBe(3));
  });

  it.each([
    ['customer', 'jobFormValid'],
    ['work', 'workValid'],
    ['control-point', 'controlPointsValid'],
  ] as const)('bypasses the %s required-field gate in draft mode', async (_gate, validityKey) => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState[validityKey] = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });

    let saved = false;
    await act(async () => {
      saved = await result.current.saveAllChanges({ mode: 'draft' });
    });

    expect(saved).toBe(true);
    expect(mutation.mutateAsync).toHaveBeenCalledTimes(1);
    expect(notify.error).not.toHaveBeenCalled();
  });

  it.each([
    ['customer', 'jobFormValid'],
    ['work', 'workValid'],
    ['control-point', 'controlPointsValid'],
  ] as const)('keeps the %s required-field gate strict by default', async (_gate, validityKey) => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState[validityKey] = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });

    let saved = true;
    await act(async () => {
      saved = await result.current.saveAllChanges();
    });

    expect(saved).toBe(false);
    expect(mutation.mutateAsync).not.toHaveBeenCalled();
    expect(notify.error).toHaveBeenCalledTimes(1);
  });

  it('shows the leave-save success notification only after the API confirms persistence', async () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    let confirmSave: ((job: JobReportSummaryViewModel) => void) | undefined;
    mutation.mutateAsync.mockReturnValue(new Promise((resolve) => {
      confirmSave = resolve;
    }));
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });

    let savePromise: Promise<boolean>;
    act(() => {
      savePromise = result.current.saveAllChanges({ mode: 'draft', notifyOnSuccess: true });
    });

    expect(notify.success).not.toHaveBeenCalled();

    await act(async () => {
      confirmSave?.(createAssignedJob(JobStatus.Draft));
      await savePromise!;
    });

    expect(notify.success).toHaveBeenCalledWith('Ændringerne er gemt', { id: 'job-draft-save-success' });
  });

  it('keeps a failed draft save blocking and does not show a success notification', async () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    mutation.mutateAsync.mockRejectedValue(new Error('Save failed'));
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });

    let saved = true;
    await act(async () => {
      saved = await result.current.saveAllChanges({ mode: 'draft', notifyOnSuccess: true });
    });

    expect(saved).toBe(false);
    expect(result.current.hasUnsavedChanges).toBe(true);
    expect(result.current.saveStatus).toBe('error');
    expect(notify.error).toHaveBeenCalledWith('Kunne ikke gemme ændringer', { id: 'job-save-error' });
    expect(notify.success).not.toHaveBeenCalled();
  });

  it('notifies when an active autosave completed before the leave-save callback runs', async () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    act(() => {
      testState.patchOnSuccess?.(createAssignedJob(JobStatus.Draft));
    });

    let saved = false;
    await act(async () => {
      saved = await result.current.saveAllChanges({ mode: 'draft', notifyOnSuccess: true });
    });

    expect(saved).toBe(true);
    expect(notify.success).toHaveBeenCalledWith('Ændringerne er gemt', { id: 'job-draft-save-success' });
  });

  it('keeps navigation blocked when the draft changes during the leave-save request', async () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    let confirmSave: ((job: JobReportSummaryViewModel) => void) | undefined;
    mutation.mutateAsync.mockReturnValue(new Promise((resolve) => {
      confirmSave = resolve;
    }));
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    let savePromise: Promise<boolean>;
    act(() => {
      savePromise = result.current.saveAllChanges({ mode: 'draft', notifyOnSuccess: true });
    });
    act(() => {
      result.current.updateCustomerObservations('Newer change');
      testState.sameFormWithoutWork = false;
    });

    let saved = true;
    await act(async () => {
      confirmSave?.(createAssignedJob(JobStatus.Draft));
      saved = await savePromise!;
    });

    expect(saved).toBe(false);
    expect(result.current.hasUnsavedChanges).toBe(true);
    expect(result.current.saveStatus).toBe('idle');
    expect(notify.success).not.toHaveBeenCalled();
  });

  it('preserves newer work changes when a non-work autosave completes', async () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.toggleControlPoint('cp-1');
    });
    act(() => {
      testState.patchOnSuccess?.(createAssignedJob(JobStatus.Draft));
    });

    expect(result.current.form.work.controlPointSelections['cp-1']).toBe(true);
  });
});
