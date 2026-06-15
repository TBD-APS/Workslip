import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { AxiosError } from 'axios';
import { toast } from 'sonner';
import {
  usePostApiJobs,
  usePostApiJobsIdAssign,
  usePostApiJobsIdLinks,
} from '../../../api/generated/jobs/jobs';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { useGetApiReferenceData } from '../../../api/generated/reference-data/reference-data';
import { useAuth } from '../../../providers/useAuth';
import { useIsAdmin } from '../../../providers/permissions';
import { useTimedStatus } from '../../../hooks/useTimedStatus';
import { emptyForm, emptySnapshot, isValidCreateForm } from '../utils';
import type { CustomerSearchViewModel, CreateJobRequest, CustomerInfo } from '../../../api/generated/models';
import type { CustomerSnapshotData } from '../customerSnapshotData';
import type { JobForm } from '../types';

type CreateJobRequestWithSnapshot = CreateJobRequest & {
  customerSnapshot?: CustomerSnapshotData | null;
};

export function useJobCreate(onCreated: (jobId: string) => void) {
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const isAdmin = useIsAdmin();
  const referenceDataQuery = useGetApiReferenceData();
  const referenceData = referenceDataQuery.data ?? null;
  const usersQuery = useGetApiUsers({ limit: 20 }, { query: { enabled: isAdmin } });
  const userEmail = user?.email ?? null;
  const assignableUsers = usersQuery.data?.users ?? [];
  const defaultAssignedUserIds = useMemo(() => {
    if (!isAdmin || !userEmail) return [];
    const currentUser = assignableUsers.find((assignableUser) => assignableUser.email === userEmail);
    return currentUser ? [currentUser.id] : [];
  }, [assignableUsers, isAdmin, userEmail]);
  const [form, setForm] = useState<JobForm>(emptyForm);
  const [linkedJobIds, setLinkedJobIds] = useState<string[]>([]);
  const [assignedUserIdsDraft, setAssignedUserIdsDraft] = useState<string[] | null>(null);
  const assignedUserIds = assignedUserIdsDraft ?? defaultAssignedUserIds;
  const [isSaving, setIsSaving] = useState(false);
  const [linksStatus, setLinksStatus] = useTimedStatus();
  const [assignmentStatus, setAssignmentStatus] = useTimedStatus();
  const createMutation = usePostApiJobs({
    mutation: {
      onSuccess: (response) => {
        const jobId = response.id;

        const promises: Promise<unknown>[] = [];

        if (linkedJobIds.length > 0) {
          promises.push(linkMutation.mutateAsync({ id: jobId, data: { targetReportIds: linkedJobIds } }));
        }

        if (assignedUserIds.length > 0) {
          promises.push(assignMutation.mutateAsync({ id: jobId, data: { userIds: assignedUserIds } }));
        }

        Promise.all(promises).then(() => {
          queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
          setIsSaving(false);
          toast.success('Sagen er oprettet');
          onCreated(jobId);
        });
      },
      onError: (error) => {
        setIsSaving(false);
        toast.error(getCreateErrorMessage(error), { id: 'job-create-error' });
      },
    },
  });

  const linkMutation = usePostApiJobsIdLinks({
    mutation: {
      onSuccess: () => setLinksStatus('saved'),
    },
  });

  const assignMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: () => setAssignmentStatus('saved'),
    },
  });

  const selectCustomer = (customer: CustomerSearchViewModel) => {
    setForm((prev) => ({
      ...prev,
      customer: {
        ...prev.customer,
        customerId: customer.id ?? null,
        name: customer.name ?? null,
        address: customer.address ?? null,
        email: customer.email ?? null,
        phone: customer.phone ?? null,
        contactPerson: customer.contactPerson ?? null,
      },
      customerSnapshot: null,
      editSnapshot: false,
    }));
  };

  const updateCustomerField = (field: keyof CustomerInfo, value: string) => {
    setForm((prev) => ({
      ...prev,
      customer: {
        ...prev.customer,
        [field]: value,
      },
    }));
  };

  const updateSnapshotField = (field: keyof CustomerSnapshotData, value: string) => {
    setForm((prev) => ({
      ...prev,
      customerSnapshot: {
        ...(prev.customerSnapshot ?? emptySnapshot),
        [field]: value,
      },
    }));
  };

  const updateEditSnapshot = (edit: boolean) => {
    setForm((prev) => ({
      ...prev,
      editSnapshot: edit,
      customerSnapshot: edit ? prev.customerSnapshot : null,
    }));
  };

  const updateReportNumber = (value: string) => {
    setForm((prev) => ({ ...prev, reportNumber: value }));
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

  const save = () => {
    if (!canSave) return;

    const request: CreateJobRequestWithSnapshot = {
      customer: {
        customerId: form.customer.customerId,
        name: form.customer.name?.trim() || null,
        address: form.customer.address?.trim() || null,
        email: form.customer.email?.trim() || null,
        contactPerson: form.customer.contactPerson?.trim() || null,
        phone: form.customer.phone?.trim() || null,
      },
      customerSnapshot: form.editSnapshot ? form.customerSnapshot : null,
      reportNumber: form.reportNumber.trim() || null,
      work: null,
      observations: {
        reportDate: null,
        taskDescription: form.taskDescription.trim() || null,
        customerObservations: form.customerObservations.trim() || null,
        technicalObservations: form.technicalObservations.trim() || null,
      },
    };

    setIsSaving(true);
    createMutation.mutate({ data: request });
  };

  const reset = () => {
    setForm(emptyForm);
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
    updateCustomerField,
    updateSnapshotField,
    updateEditSnapshot,
    updateReportNumber,
    updateTaskDescription,
    updateCustomerObservations,
    updateTechnicalObservations,
    updateLinkedJobs,
    updateAssignedUsers,
    updateWorkCategories,
    updateWorkKind,
    updateCustomWorkKind,
    save,
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
