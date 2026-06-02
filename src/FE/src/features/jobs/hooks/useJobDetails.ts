import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  getGetApiJobsQueryKey,
  getGetApiJobsIdQueryKey,
  useGetApiJobs,
  useGetApiJobsId,
  usePostApiJobsIdAssign,
  usePostApiJobsIdLinks,
  usePatchApiJobsId,
} from '../../../api/generated/jobs/jobs';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { useTimedStatus } from '../../../hooks/useTimedStatus';
import {
  emptyForm,
  getLinkableJobs,
  getResponseData,
  getUserList,
  isValidContactInfo,
  sameForm,
  toForm,
  toNullable,
  toUpdateRequest,
} from '../utils';
import type { CustomerInfo, JobReportSummaryViewModel } from '../../../api/generated/models';
import type { JobForm } from '../types';

type JobDetailsDraft = { jobId: string; form: JobForm };
type AssignmentDraft = { jobId: string; userIds: string[] };
type LinksDraft = { jobId: string; linkedJobIds: string[] };

export function useJobDetails(jobId: string | undefined) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<JobDetailsDraft | null>(null);
  const [currentStep, setCurrentStep] = useState(0);
  const [saveStatus, setSaveStatus] = useTimedStatus();
  const [assignmentStatus, setAssignmentStatus] = useTimedStatus();
  const [linksStatus, setLinksStatus] = useTimedStatus();
  const [assignmentDraft, setAssignmentDraft] = useState<AssignmentDraft | null>(null);
  const [linksDraft, setLinksDraft] = useState<LinksDraft | null>(null);
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const query = useGetApiJobsId(jobId ?? '', {
    query: { enabled: Boolean(jobId) },
  });
  const job = getResponseData<JobReportSummaryViewModel>(query.data);
  const usersQuery = useGetApiUsers();
  const jobsQuery = useGetApiJobs({ limit: 200 });
  const assignableUsers = getUserList(usersQuery.data);
  const linkableJobs = getLinkableJobs(jobsQuery.data, jobId);
  const initialForm = job ? toForm(job) : null;
  const form =
    draft && draft.jobId === jobId ? draft.form : initialForm ?? emptyForm;
  const assignedUserIds =
    assignmentDraft && assignmentDraft.jobId === jobId
      ? assignmentDraft.userIds
      : job?.assignedUsers.map((user) => user.id) ?? [];
  const linkedJobIds =
    linksDraft && linksDraft.jobId === jobId
      ? linksDraft.linkedJobIds
      : job?.links.map((link) => link.linkedReportId) ?? [];

  const mutation = usePatchApiJobsId({
    mutation: {
      onSuccess: () => {
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
          queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
        }
        setDraft(null);
        setSaveStatus('saved');
      },
      onError: () => {
        setSaveStatus('error');
        toast.error('Kunne ikke gemme ændringer', { id: 'job-save-error' });
      },
    },
  });

  const assignmentMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: () => {
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
          queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
        }
        setAssignmentStatus('saved');
      },
      onError: () => {
        setAssignmentStatus('error');
        toast.error('Kunne ikke opdatere tildeling', { id: 'job-assign-error' });
      },
    },
  });

  const linkMutation = usePostApiJobsIdLinks({
    mutation: {
      onSuccess: () => {
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
        }
        setLinksStatus('saved');
      },
      onError: () => {
        setLinksStatus('error');
        toast.error('Kunne ikke opdatere tilknyttede sager', { id: 'job-links-error' });
      },
    },
  });

  useEffect(() => {
    if (!draft || !initialForm || !job || !jobId) return;

    debounceTimerRef.current = setTimeout(() => {
      if (sameForm(initialForm, draft.form)) {
        setDraft(null);
        return;
      }

      if (!isValidContactInfo(draft.form.customer)) {
        setSaveStatus('error');
        return;
      }

      setSaveStatus('saving');
      mutation.mutate({ id: jobId, data: toUpdateRequest(job, initialForm, draft.form) });
    }, 1500);

    return () => clearTimeout(debounceTimerRef.current);
  }, [draft, initialForm, job, jobId, mutation, setSaveStatus]);

  useEffect(() => {
    return () => clearTimeout(debounceTimerRef.current);
  }, []);

  const updateDraft = (nextForm: JobForm) => {
    if (!jobId) return;
    setDraft({ jobId, form: nextForm });
    if (saveStatus === 'saved') setSaveStatus('idle');
  };

  const updateCustomer = (field: keyof CustomerInfo, value: string | null) => {
    updateDraft({
      ...form,
      customer: { ...form.customer, [field]: toNullable(value) },
    });
  };

  const updateReportNumber = (value: string) => {
    updateDraft({ ...form, reportNumber: value });
  };

  const updateTaskDescription = (value: string) => {
    updateDraft({ ...form, taskDescription: value });
  };

  const updateCustomerObservations = (value: string) => {
    updateDraft({ ...form, customerObservations: value });
  };

  const updateAssignedUsers = (userIds: string[]) => {
    if (!jobId) return;
    setAssignmentDraft({ jobId, userIds });
    setAssignmentStatus('saving');
    assignmentMutation.mutate({ id: jobId, data: { userIds } });
  };

  const updateLinkedJobs = (linkedJobIds: string[]) => {
    if (!jobId || !job) return;

    const existingLinkedIds = job.links.map((link) => link.linkedReportId);
    const addedIds = linkedJobIds.filter((id) => !existingLinkedIds.includes(id));

    setLinksDraft({ jobId, linkedJobIds });
    if (addedIds.length === 0) return;

    setLinksStatus('saving');
    addedIds.forEach((targetReportId) => {
      linkMutation.mutate({ id: jobId, data: { targetReportId, linkType: 'related' } });
    });
  };

  const flushSave = () => {
    clearTimeout(debounceTimerRef.current);
    if (!draft || !initialForm || !job || !jobId) return;
    if (sameForm(initialForm, draft.form)) {
      setDraft(null);
      return;
    }
    if (!isValidContactInfo(draft.form.customer)) {
      setSaveStatus('error');
      return;
    }
    setSaveStatus('saving');
    mutation.mutate({ id: jobId, data: toUpdateRequest(job, initialForm, draft.form) });
  };

  return {
    job,
    form,
    assignableUsers,
    assignedUserIds,
    linkableJobs,
    linkedJobIds,
    currentStep,
    setCurrentStep,
    isLoading: query.isLoading,
    isError: query.isError,
    isLoadingUsers: usersQuery.isLoading,
    isLoadingJobs: jobsQuery.isLoading,
    saveStatus,
    assignmentStatus,
    linksStatus,
    reportNumberReadOnly: Boolean(job?.reportNumber),
    flushSave,
    updateAssignedUsers,
    updateLinkedJobs,
    updateCustomer,
    updateReportNumber,
    updateTaskDescription,
    updateCustomerObservations,
  };
}
