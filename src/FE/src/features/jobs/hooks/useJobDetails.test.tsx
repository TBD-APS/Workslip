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
}));

const mutation = vi.hoisted(() => ({
  error: null,
  isPending: false,
  mutate: vi.fn(),
  mutateAsync: vi.fn(),
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
  usePatchApiJobsId: () => mutation,
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
  notify: { error: vi.fn(), success: vi.fn() },
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
    isValidJobForm: () => true,
    isValidWork: () => true,
    sameForm: () => true,
    sameFormWithoutWork: () => true,
    toForm: () => form,
    toUpdateRequest: vi.fn(),
  };
});

vi.mock('../components/steps/controlPointsValidation', () => ({
  validateControlPoints: () => ({ valid: true }),
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

describe('useJobDetailsState worksheet shortcut', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    testState.job = undefined;
    testState.referenceData = undefined;
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
});
