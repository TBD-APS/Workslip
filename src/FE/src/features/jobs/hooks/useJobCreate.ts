import { useCallback, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { AxiosError } from 'axios';
import { notify } from '../../../lib/toast';
import {
  usePostApiJobs,
  usePostApiJobsIdAssign,
  usePostApiJobsIdLinks,
  getGetApiJobsQueryKey,
} from '../../../api/generated/jobs/jobs';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { useGetApiReferenceData } from '../../../api/generated/reference-data/reference-data';
import { useAuth } from '../../../providers/useAuth';
import { canReceiveJobAssignment, useIsAdmin } from '../../../providers/permissions';
import { useTimedStatus } from '../../../hooks/useTimedStatus';
import { emptyForm, isValidCreateForm } from '../utils';
import { validateEmail, validatePhoneNumber } from '../../../components/forms/validators';
import type { CreateJobRequest } from '../../../api/generated/models';
import type { CustomerSnapshotData } from '../../../api/generated/models/customerSnapshotData';
import type { JobForm, WorksheetDraft } from '../types';
import { useCustomerSnapshot, hasSnapshotData, trimSnapshot } from './useCustomerSnapshot';

type CreateJobRequestWithSnapshot = CreateJobRequest & {
  customerSnapshot?: CustomerSnapshotData | null;
  createCustomerFromSnapshot?: boolean;
  jobType: 'KLS' | 'Diverse' | 'Unknown';
  assignedUserIds?: string[];
};

export function useJobCreate(onCreated: (jobId: string) => void, initialForm?: JobForm) {
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const isAdmin = useIsAdmin();
  const referenceDataQuery = useGetApiReferenceData();
  const referenceData = referenceDataQuery.data ?? null;
  const usersQuery = useGetApiUsers({ limit: 200 }, { query: { enabled: isAdmin } });
  const assignableUsers = (usersQuery.data?.users ?? []).filter((candidate) => canReceiveJobAssignment(candidate.role));
  const defaultAssignedUserIds = user?.id && canReceiveJobAssignment(user.role) ? [user.id] : [];
  const [form, setForm] = useState<JobForm>(initialForm ?? emptyForm);
  const [linkedJobIds, setLinkedJobIds] = useState<string[]>([]);
  const [assignedUserIdsDraft, setAssignedUserIdsDraft] = useState<string[] | null>(null);
  const assignedUserIds = assignedUserIdsDraft ?? defaultAssignedUserIds;
  const [isSaving, setIsSaving] = useState(false);
  const [linksStatus, setLinksStatus] = useTimedStatus();
  const [assignmentStatus, setAssignmentStatus] = useTimedStatus();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const createMutation = usePostApiJobs({
    mutation: {
      onSuccess: (response) => {
        const jobId = response.id;
        const reportNumber = response.reportNumber;
        const promises: Promise<unknown>[] = [];

        if (linkedJobIds.length > 0) {
          promises.push(linkMutation.mutateAsync({ id: jobId, data: { targetReportIds: linkedJobIds } }));
        }

        // New backends persist initial assignments inside the job-create transaction.
        // Keep this conditional fallback so a newer frontend remains safe during a short
        // frontend-before-backend deployment skew instead of silently losing assignment.
        const persistedAssignedIds = new Set((response.assignedUsers ?? []).map((candidate) => candidate.id));
        const assignmentAlreadyPersisted =
          persistedAssignedIds.size === assignedUserIds.length
          && assignedUserIds.every((id) => persistedAssignedIds.has(id));
        if (!assignmentAlreadyPersisted) {
          promises.push(assignMutation.mutateAsync({ id: jobId, data: { userIds: assignedUserIds } }));
        }

        Promise.all(promises).then(() => {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
          queryClient.invalidateQueries({ queryKey: ['worksheets'] });
          setIsSaving(false);
          notify.success(reportNumber ? `Sag ${reportNumber} er oprettet` : 'Sagen er oprettet');
          onCreated(jobId);
        }).catch((error) => {
          setIsSaving(false);
          notify.error('Sagen er oprettet, men tildeling eller sammenkædning mislykkedes', { id: 'job-create-followup-error' });
          console.error('Job create follow-up failed:', error);
          onCreated(jobId);
        });
      },
      onError: (error) => {
        setIsSaving(false);
        notify.error(getCreateErrorMessage(error), { id: 'job-create-error' });
      },
    },
    request: { skipGlobalErrorToast: true },
  });

  const linkMutation = usePostApiJobsIdLinks({
    mutation: {
      onSuccess: () => setLinksStatus('saved'),
    },
    request: { skipGlobalErrorToast: true },
  });

  const assignMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: () => setAssignmentStatus('saved'),
    },
    request: { skipGlobalErrorToast: true },
  });

  const { selectCustomer, updateEditSnapshot } = useCustomerSnapshot(setForm);

  const clearFieldError = useCallback((field: string) => {
    setFieldErrors((prev) => {
      if (!prev[field]) return prev;
      const next = { ...prev };
      delete next[field];
      return next;
    });
  }, []);

  const createNewCustomer = () => {
    setForm((prev) => ({
      ...prev,
      customerId: null,
      customerSnapshot: { name: null, email: null, phone: null, address: null, contactPerson: null },
      editSnapshot: true,
      createCustomer: false,
    }));
  };

  const updateCreateCustomer = (value: boolean) => {
    setForm((prev) => ({ ...prev, createCustomer: value }));
  };

  const updateDestinationAddress = (value: string) => {
    setForm((prev) => ({ ...prev, destinationAddress: value }));
    clearFieldError('destinationAddress');
  };

  const updateDestinationZipCode = (value: string) => {
    setForm((prev) => ({ ...prev, destinationZipCode: value }));
    clearFieldError('destinationZipCode');
  };

  const updateDestinationCity = (value: string) => {
    setForm((prev) => ({ ...prev, destinationCity: value }));
  };

  const updateJobType = (value: 'KLS' | 'Diverse') => {
    setForm((prev) => ({ ...prev, jobType: value }));
    if (value === 'Diverse') {
      setFieldErrors((prev) => {
        const next = { ...prev };
        delete next.customerName;
        delete next.email;
        delete next.phone;
        return next;
      });
    }
  };

  const updateTimesheets = (timesheets: WorksheetDraft[]) => {
    setForm((prev) => ({ ...prev, timesheets }));
  };

  const updateTaskDescription = (value: string) => {
    setForm((prev) => ({ ...prev, taskDescription: value }));
  };

  const updateCustomerObservations = (value: string) => {
    setForm((prev) => ({ ...prev, customerObservations: value }));
  };

  const updateTechnicalObservations = (value: string) => {
    setForm((prev) => ({ ...prev, technicalObservations: value }));
  };

  const updateSnapshotField = useCallback(
    (field: keyof CustomerSnapshotData, value: string) => {
      setForm((prev) => ({
        ...prev,
        customerSnapshot: {
          ...(prev.customerSnapshot ?? { name: null, email: null, phone: null, address: null, contactPerson: null }),
          [field]: value,
        },
        editSnapshot: true,
      }));
      const fieldKey = field === 'name' ? 'customerName' : field;
      clearFieldError(fieldKey);
    },
    [clearFieldError],
  );

  const updateLinkedJobs = (jobIds: string[]) => {
    setLinkedJobIds(jobIds);
    setLinksStatus('idle');
  };

  const updateAssignedUsers = (userIds: string[]) => {
    if (!isAdmin) return;
    setAssignedUserIdsDraft(userIds);
    setAssignmentStatus('idle');
  };

  const updateWorkCategories = (categoryIds: string[]) => {
    setForm((prev) => ({ ...prev, work: { ...prev.work, categoryIds } }));
  };

  const updateWorkKind = (workKind: string) => {
    const selectedWorkKind = referenceData?.workKinds.find((kind) => kind.normalizedLabel === workKind);
    setForm((prev) => ({
      ...prev,
      work: {
        ...prev.work,
        workKind,
        customWorkKind: selectedWorkKind?.requiresCustomWorkKind ? prev.work.customWorkKind : '',
      },
    }));
  };

  const updateCustomWorkKind = (customWorkKind: string) => {
    setForm((prev) => ({ ...prev, work: { ...prev.work, customWorkKind } }));
  };

  const canSave = isValidCreateForm(form);

  function computeFieldErrors(targetForm: JobForm): Record<string, string> {
    const errors: Record<string, string> = {};

    if (targetForm.jobType === 'KLS') {
      const name = targetForm.customerSnapshot?.name ?? null;
      const email = targetForm.customerSnapshot?.email ?? null;
      const phone = targetForm.customerSnapshot?.phone ?? null;

      if ((name?.trim().length ?? 0) === 0) errors.customerName = 'Kundenavn er påkrævet';
      if (validateEmail(email) !== null) errors.email = validateEmail(email)!;
      if (validatePhoneNumber(phone) !== null) errors.phone = validatePhoneNumber(phone)!;
    }

    return errors;
  }

  const saveForm = (targetForm: JobForm) => {
    const errors = computeFieldErrors(targetForm);
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      const firstKey = Object.keys(errors)[0];
      setTimeout(() => {
        const el = document.querySelector(`[data-field-error="${firstKey}"]`);
        if (el) {
          el.scrollIntoView({ behavior: 'smooth', block: 'center' });
          (el as HTMLElement)?.focus?.();
        }
      }, 100);
      return;
    }
    if (!user?.id) {
      notify.error('Bruger ikke fundet. Log ind igen.', { id: 'job-create-no-user' });
      return;
    }
    setFieldErrors({});

    const request: CreateJobRequestWithSnapshot = {
      customerId: targetForm.customerId,
      customerSnapshot: hasSnapshotData(targetForm.customerSnapshot)
        ? trimSnapshot(targetForm.customerSnapshot)
        : null,
      createCustomerFromSnapshot: targetForm.createCustomer || undefined,
      destinationAddress: targetForm.destinationAddress.trim() || null,
      destinationZipCode: targetForm.destinationZipCode.trim() || null,
      destinationCity: targetForm.destinationCity.trim() || null,
      jobType: targetForm.jobType,
      assignedUserIds,
      work: null,
      observations: {
        reportDate: null,
        taskDescription: targetForm.taskDescription.trim() || null,
        customerObservations: targetForm.customerObservations.trim() || null,
        technicalObservations: targetForm.technicalObservations.trim() || null,
      },
      ...(targetForm.jobType === 'Diverse' && targetForm.timesheets.length > 0
        ? {
            timesheets: targetForm.timesheets.map(ts => ({
              workDate: ts.workDate,
              userId: ts.userId,
              hoursWorked: typeof ts.hours === 'number' ? ts.hours : Number(String(ts.hours).replace(',', '.')),
              sleptOnJob: ts.sleptOnJob,
            })),
          }
        : {}),
    };

    setIsSaving(true);
    createMutation.mutate({ data: request });
  };

  const save = () => {
    saveForm(form);
  };

  const saveWithTimesheets = (timesheets: WorksheetDraft[]) => {
    const nextForm = { ...form, timesheets };
    setForm(nextForm);
    saveForm(nextForm);
  };

  const reset = (preserve?: { customerId?: string | null; customerSnapshot?: CustomerSnapshotData | null }) => {
    setForm(() => ({
      ...emptyForm,
      customerId: preserve?.customerId ?? emptyForm.customerId,
      customerSnapshot: preserve?.customerSnapshot ?? emptyForm.customerSnapshot,
    }));
    setLinkedJobIds([]);
    setAssignedUserIdsDraft(null);
    setIsSaving(false);
    setLinksStatus('idle');
    setAssignmentStatus('idle');
  };

  return {
    form,
    linkedJobIds,
    assignedUserIds,
    assignableUsers,
    isSaving,
    canSave,
    linksStatus,
    assignmentStatus,
    referenceData,
    isLoadingReferenceData: referenceDataQuery.isLoading,
    isLoadingUsers: usersQuery.isLoading,
    selectCustomer,
    createNewCustomer,
    updateSnapshotField,
    updateEditSnapshot,
    updateCreateCustomer,
    updateDestinationAddress,
    updateDestinationZipCode,
    updateDestinationCity,
    updateJobType,
    updateTimesheets,
    updateTaskDescription,
    updateCustomerObservations,
    updateTechnicalObservations,
    updateLinkedJobs,
    updateAssignedUsers,
    updateWorkCategories,
    updateWorkKind,
    updateCustomWorkKind,
    fieldErrors,
    save,
    saveWithTimesheets,
    reset,
  };
}

function getCreateErrorMessage(error: unknown) {
  const axiosError = error as AxiosError<{ error?: string }>;
  if (axiosError.response?.status === 409 && axiosError.response.data?.error === 'duplicate_report_number') {
    return 'Sagsnummeret findes allerede.';
  }

  return 'Kunne ikke oprette sagen';
}
