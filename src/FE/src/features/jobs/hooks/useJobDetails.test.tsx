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
  sameWork: true,
  jobFormValid: true,
  workValid: true,
  controlPointsValid: true,
  patchOnSuccess: undefined as ((data: JobReportSummaryViewModel) => void) | undefined,
}));

const mutation = vi.hoisted(() => ({
  error: null,
  isPending: false,
  mutate: vi.fn<(variables: unknown, options?: { onSettled?: () => void }) => void>(),
  mutateAsync: vi.fn(),
  // Settlement callback of the last `mutate`, so a test can land the request it
  // issued instead of leaving it in flight for the rest of the test.
  settleLastMutate: undefined as (() => void) | undefined,
}));

const notify = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
}));

const utils = vi.hoisted(() => ({
  toUpdateRequest: vi.fn(),
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
      allIrrelevantReason: '',
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
    sameWork: () => testState.sameWork,
    toForm: () => form,
    toUpdateRequest: utils.toUpdateRequest,
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

type JobDetailsState = ReturnType<typeof useJobDetailsState>;

const staleErrorCases: [label: string, applyChange: (state: JobDetailsState) => void][] = [
  ['a plain draft update', (state) => state.updateTechnicalObservations('Newer change')],
  ['a functional form update', (state) => state.updateDestinationAddress('Nyvej 1')],
];

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
    testState.sameWork = true;
    testState.jobFormValid = true;
    testState.workValid = true;
    testState.controlPointsValid = true;
    testState.patchOnSuccess = undefined;
    mutation.isPending = false;
    mutation.settleLastMutate = undefined;
    mutation.mutate.mockImplementation((_variables, options) => {
      mutation.settleLastMutate = () => options?.onSettled?.();
    });
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
    // A refusal that issued no request is not a failed save.
    expect(result.current.saveStatus).toBe('idle');
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

  it('lands a backward step move even when the current step cannot be saved', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.jobFormValid = false;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.setCurrentStep(2);
    });
    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    act(() => {
      result.current.navigateToStep(1);
    });

    expect(result.current.currentStep).toBe(1);
    expect(result.current.hasUnsavedChanges).toBe(true);
    expect(mutation.mutate).not.toHaveBeenCalled();
  });

  it('keeps a forward step move blocked when the current step cannot be saved', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.jobFormValid = false;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.setCurrentStep(2);
    });
    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    act(() => {
      result.current.navigateToStep(3);
    });

    // The hook keeps no step gate of its own: what refuses the move is the save
    // it could not get out. Naming the offending step is the caller's job - the
    // dots' one range styles, names and bounces every locked step, and the
    // Naeste button is gated on the current step's own issues - so the hook no
    // longer adds a second, differently-scoped refusal with a toast that names
    // no step.
    expect(result.current.currentStep).toBe(2);
    expect(mutation.mutate).not.toHaveBeenCalled();
    expect(notify.error).not.toHaveBeenCalled();
    expect(result.current.saveStatus).toBe('idle');
    expect(result.current.hasUnsavedChanges).toBe(true);
  });

  it('does not issue a second PATCH while the same form is already in flight', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    const { result, rerender } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });

    let firstFlush = false;
    act(() => {
      firstFlush = result.current.flushSave();
    });

    expect(firstFlush).toBe(true);
    expect(mutation.mutate).toHaveBeenCalledTimes(1);

    mutation.isPending = true;
    act(() => {
      rerender();
    });

    let secondFlush = false;
    act(() => {
      secondFlush = result.current.flushSave();
    });

    expect(secondFlush).toBe(true);
    expect(mutation.mutate).toHaveBeenCalledTimes(1);
  });

  it('issues one PATCH when two flushes land in the same tick', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });

    let firstFlush = false;
    let secondFlush = false;
    act(() => {
      // Both calls read the same render, where `mutation.isPending` is still
      // false - the render-scoped flag a double-fire used to slip past.
      firstFlush = result.current.flushSave();
      secondFlush = result.current.flushSave();
    });

    expect(firstFlush).toBe(true);
    expect(secondFlush).toBe(true);
    expect(mutation.mutate).toHaveBeenCalledTimes(1);
  });

  it('re-arms the writer only once the request it protected settles', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    act(() => {
      result.current.flushSave();
    });

    expect(mutation.mutate).toHaveBeenCalledTimes(1);

    let flushWhileInFlight = false;
    act(() => {
      flushWhileInFlight = result.current.flushSave();
    });

    expect(flushWhileInFlight).toBe(true);
    expect(mutation.mutate).toHaveBeenCalledTimes(1);

    act(() => {
      mutation.settleLastMutate?.();
    });
    act(() => {
      result.current.flushSave();
    });

    expect(mutation.mutate).toHaveBeenCalledTimes(2);
  });

  it('keeps the in-flight guard armed when an unrelated response lands', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    act(() => {
      result.current.flushSave();
    });

    expect(mutation.mutate).toHaveBeenCalledTimes(1);

    // A response this PATCH did not produce - a debounced autosave or a
    // leave-save landing - must not disarm a request that is still in flight.
    act(() => {
      testState.patchOnSuccess?.(createAssignedJob(JobStatus.Draft));
    });

    let flushed = false;
    act(() => {
      flushed = result.current.flushSave();
    });

    expect(flushed).toBe(true);
    expect(mutation.mutate).toHaveBeenCalledTimes(1);
  });

  it('keeps a work-only draft when a step-0 flush leaves work out of the comparison', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    // Only the work slice differs, and a step-0 flush compares
    // `sameFormWithoutWork` - so the no-op branch must not throw away the slice
    // it never compared.
    testState.sameForm = false;
    testState.sameFormWithoutWork = true;
    testState.sameWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      // The worksheet shortcut lands this job on step 3 on mount; the silent
      // discard this pins happens while the user stands on step 0.
      result.current.setCurrentStep(0);
    });
    act(() => {
      result.current.updateWorkKind('Service');
    });

    let flushed = false;
    act(() => {
      // currentStep 0 means includeWork: false, the branch that used to run
      // setDraft(null) and report success on every blocked-Naeste bounce.
      flushed = result.current.saveCurrentStep();
    });

    expect(flushed).toBe(true);
    expect(mutation.mutate).not.toHaveBeenCalled();
    expect(result.current.form.work.workKind).toBe('Service');
    expect(result.current.hasUnsavedChanges).toBe(true);
  });

  it('flushes the pending work edit when jumping to a validation issue', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    // Only the work slice changed - the state that used to send jumpToStep down
    // the sameFormWithoutWork branch, where setDraft(null) deleted the edit.
    testState.sameForm = false;
    testState.sameFormWithoutWork = true;
    testState.sameWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.setCurrentStep(1);
    });
    act(() => {
      result.current.updateWorkKind('Service');
    });
    act(() => {
      result.current.jumpToStep(1);
    });

    expect(mutation.mutate).toHaveBeenCalledTimes(1);
    expect(utils.toUpdateRequest).toHaveBeenLastCalledWith(
      expect.anything(),
      expect.anything(),
      expect.anything(),
      expect.anything(),
      { includeWork: true },
    );
    expect(result.current.form.work.workKind).toBe('Service');
  });

  it('sends a work-carrying step flush no matter what the catalogue holds', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    testState.sameWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.setCurrentStep(1);
    });
    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });

    let flushed = false;
    act(() => {
      flushed = result.current.flushSave({ includeWork: true });
    });

    // No writer withholds a write over serialisation any more: `toWorkRequest`
    // sends `installationTypes: null` when the catalogue cannot resolve the
    // selection, so the PATCH goes out, the rest of the work slice applies and
    // nothing keeps the draft pending for ever. Withholding it here was the
    // state a sag could neither be saved from nor left.
    expect(flushed).toBe(true);
    expect(mutation.mutate).toHaveBeenCalledTimes(1);
    expect(utils.toUpdateRequest).toHaveBeenLastCalledWith(
      expect.anything(),
      expect.anything(),
      expect.anything(),
      expect.anything(),
      { includeWork: true },
    );
    expect(notify.error).not.toHaveBeenCalled();
  });

  it('fires no save-path toast when jumping to a validation issue', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    testState.sameWork = false;
    testState.workValid = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.setCurrentStep(1);
    });
    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    act(() => {
      result.current.jumpToStep(0);
    });

    // The caller has already shown the bounce toast that names the issue being
    // jumped to, and the jump is never gated on the save - so a second toast
    // about that save would stack under a different sonner id and name
    // something the wizard has already moved past.
    expect(result.current.currentStep).toBe(0);
    expect(notify.error).not.toHaveBeenCalled();
  });

  it('lets a leave-save land no matter what the catalogue holds', async () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameWork = false;
    const { result } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });

    let saved = false;
    await act(async () => {
      saved = await result.current.saveAllChanges({ mode: 'draft', notifyOnSuccess: true });
    });

    // The leave path is the one that must never refuse: NavigationGuard's
    // auto-save mode turns a `false` into `blocker.reset()`, and its modal has
    // no "Forlad uden at gemme" button - so a refusal here left the user unable
    // to both save and leave.
    expect(saved).toBe(true);
    expect(mutation.mutateAsync).toHaveBeenCalledTimes(1);
    expect(notify.success).toHaveBeenCalledWith('Ændringerne er gemt', { id: 'job-draft-save-success' });
    expect(notify.error).not.toHaveBeenCalled();
  });

  it('runs the work gate before short-circuiting on an in-flight save', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    const { result, rerender } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    act(() => {
      result.current.flushSave();
    });

    expect(mutation.mutate).toHaveBeenCalledTimes(1);

    mutation.isPending = true;
    act(() => {
      rerender();
    });
    testState.workValid = false;

    let flushed = true;
    act(() => {
      flushed = result.current.flushSave({ includeWork: true, validateWork: true });
    });

    // 'already saving' must never be read as 'valid, safe to advance'.
    expect(flushed).toBe(false);
    expect(mutation.mutate).toHaveBeenCalledTimes(1);
    expect(notify.error).toHaveBeenCalledWith(
      'Udfyld anlægstyper og opgavetype',
      { id: 'job-work-validation-error' },
    );
    // Nothing left the client, so the red "Fejl ved gem" chip stays off: the
    // toast is the whole message.
    expect(result.current.saveStatus).toBe('idle');
  });

  it('still sends the work slice when the in-flight save carried none', () => {
    testState.job = createAssignedJob(JobStatus.Draft);
    testState.referenceData = referenceData;
    testState.sameForm = false;
    testState.sameFormWithoutWork = false;
    testState.sameWork = false;
    const { result, rerender } = renderHook(
      () => useJobDetailsState('job-1', { autoSave: false }),
      { wrapper: createWrapper() },
    );

    act(() => {
      result.current.updateTechnicalObservations('Changed');
    });
    act(() => {
      result.current.flushSave();
    });

    expect(utils.toUpdateRequest).toHaveBeenLastCalledWith(
      expect.anything(),
      expect.anything(),
      expect.anything(),
      expect.anything(),
      { includeWork: false },
    );

    mutation.isPending = true;
    act(() => {
      rerender();
    });

    let flushed = false;
    act(() => {
      flushed = result.current.flushSave({ includeWork: true });
    });

    expect(flushed).toBe(true);
    expect(mutation.mutate).toHaveBeenCalledTimes(2);
    expect(utils.toUpdateRequest).toHaveBeenLastCalledWith(
      expect.anything(),
      expect.anything(),
      expect.anything(),
      expect.anything(),
      { includeWork: true },
    );
  });

  it('leaves the save status idle when a debounced autosave is withheld locally', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      testState.job = createAssignedJob(JobStatus.Draft);
      testState.referenceData = referenceData;
      testState.jobFormValid = false;
      testState.sameForm = false;
      testState.sameFormWithoutWork = false;
      const { result } = renderHook(
        () => useJobDetailsState('job-1'),
        { wrapper: createWrapper() },
      );

      act(() => {
        result.current.updateTechnicalObservations('Changed');
      });
      act(() => {
        vi.advanceTimersByTime(1500);
      });

      expect(mutation.mutate).not.toHaveBeenCalled();
      expect(result.current.saveStatus).toBe('idle');
    } finally {
      vi.useRealTimers();
    }
  });

  it.each(staleErrorCases)('clears a stale save error after %s', async (_label, applyChange) => {
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

    await act(async () => {
      await result.current.saveAllChanges({ mode: 'draft' });
    });

    expect(result.current.saveStatus).toBe('error');

    act(() => {
      applyChange(result.current);
    });

    expect(result.current.saveStatus).toBe('idle');
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
