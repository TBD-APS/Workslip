import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  usePostApiJobs,
  usePostApiJobsIdAssign,
  usePostApiJobsIdLinks,
} from '../../../api/generated/jobs/jobs';
import { useTimedStatus } from '../../../hooks/useTimedStatus';
import { emptyCustomer, isValidContactInfo } from '../utils';
import type { CustomerInfo, CreateJobRequest } from '../../../api/generated/models';
import type { JobForm } from '../types';

export function useJobCreate(onCreated: (jobId: string) => void) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState<JobForm>({
    customer: { ...emptyCustomer },
    reportNumber: '',
    taskDescription: '',
    customerObservations: '',
  });
  const [assignedUserIds, setAssignedUserIds] = useState<string[]>([]);
  const [linkedJobIds, setLinkedJobIds] = useState<string[]>([]);
  const [isSaving, setIsSaving] = useState(false);
  const [assignmentStatus, setAssignmentStatus] = useTimedStatus();
  const [linksStatus, setLinksStatus] = useTimedStatus();

  const createMutation = usePostApiJobs({
    mutation: {
      onSuccess: (response) => {
        const jobId = (response.data as unknown as { id: string }).id;

        const doAssign =
          assignedUserIds.length > 0
            ? assignMutation.mutateAsync({ id: jobId, data: { userIds: assignedUserIds } })
            : Promise.resolve();

        const doLinks =
          linkedJobIds.length > 0
            ? Promise.all(
                linkedJobIds.map((targetReportId) =>
                  linkMutation.mutateAsync({
                    id: jobId,
                    data: { targetReportId, linkType: 'related' },
                  }),
                ),
              )
            : Promise.resolve();

        Promise.all([doAssign, doLinks]).then(() => {
          queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
          setIsSaving(false);
          toast.success('Sagen er oprettet');
          onCreated(jobId);
        });
      },
      onError: () => {
        setIsSaving(false);
        toast.error('Kunne ikke oprette sagen', { id: 'job-create-error' });
      },
    },
  });

  const assignMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: () => setAssignmentStatus('saved'),
    },
  });

  const linkMutation = usePostApiJobsIdLinks({
    mutation: {
      onSuccess: () => setLinksStatus('saved'),
    },
  });

  const updateCustomer = (field: keyof CustomerInfo, value: string | null) => {
    setForm((prev) => ({
      ...prev,
      customer: { ...prev.customer, [field]: value },
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

  const updateAssignedUsers = (userIds: string[]) => {
    setAssignedUserIds(userIds);
    setAssignmentStatus('idle');
  };

  const updateLinkedJobs = (jobIds: string[]) => {
    setLinkedJobIds(jobIds);
    setLinksStatus('idle');
  };

  const canSave = isValidContactInfo(form.customer);

  const save = () => {
    if (!canSave) return;

    const request: CreateJobRequest = {
      customer: {
        customerId: null,
        name: form.customer.name?.trim() || null,
        address: form.customer.address?.trim() || null,
        email: form.customer.email?.trim() || null,
        contactPerson: form.customer.contactPerson?.trim() || null,
        phone: form.customer.phone?.trim() || null,
      },
      reportNumber: form.reportNumber.trim() || null,
      work: null,
      observations: {
        reportDate: null,
        taskDescription: form.taskDescription.trim() || null,
        customerObservations: form.customerObservations.trim() || null,
        technicalObservations: null,
      },
    };

    setIsSaving(true);
    createMutation.mutate({ data: request });
  };

  return {
    form,
    assignedUserIds,
    linkedJobIds,
    isSaving,
    canSave,
    assignmentStatus,
    linksStatus,
    updateCustomer,
    updateReportNumber,
    updateTaskDescription,
    updateCustomerObservations,
    updateAssignedUsers,
    updateLinkedJobs,
    save,
  };
}
