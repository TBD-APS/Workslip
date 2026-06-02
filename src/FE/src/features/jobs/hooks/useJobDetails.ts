import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  getGetApiJobsIdQueryKey,
  useDeleteApiJobsIdLinksLinkId,
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
  const pendingLinksRef = useRef<Set<string>>(new Set());
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

  const initialFormRef = useRef(initialForm);
  const jobRef = useRef(job);
  const mutateRef = useRef(mutation.mutate);
  initialFormRef.current = initialForm;
  jobRef.current = job;
  mutateRef.current = mutation.mutate;

  const assignmentMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: () => {
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
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
      onSuccess: (_data, variables) => {
        pendingLinksRef.current.delete(variables.data.targetReportId);
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
        }
        if (pendingLinksRef.current.size === 0) {
          setLinksStatus('saved');
        }
      },
      onError: (_error, variables) => {
        pendingLinksRef.current.delete(variables.data.targetReportId);
        setLinksStatus('error');
        toast.error('Kunne ikke opdatere tilknyttede sager', { id: 'job-links-error' });
      },
    },
  });

  const deleteLinkMutation = useDeleteApiJobsIdLinksLinkId({
    mutation: {
      onSuccess: (_data, variables) => {
        pendingLinksRef.current.delete(variables.linkId);
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
        }
        if (pendingLinksRef.current.size === 0) {
          setLinksStatus('saved');
        }
      },
      onError: (_error, variables) => {
        pendingLinksRef.current.delete(variables.linkId);
        setLinksStatus('error');
        toast.error('Kunne ikke fjerne tilknyttet sag', { id: 'job-links-error' });
      },
    },
  });

  useEffect(() => {
    const currentInitialForm = initialFormRef.current;
    const currentJob = jobRef.current;
    const currentMutate = mutateRef.current;
    if (!draft || !currentInitialForm || !currentJob || !jobId) return;

    debounceTimerRef.current = setTimeout(() => {
      if (sameForm(currentInitialForm, draft.form)) {
        setDraft(null);
        return;
      }

      if (!isValidContactInfo(draft.form.customer)) {
        setSaveStatus('error');
        return;
      }

      setSaveStatus('saving');
      currentMutate({ id: jobId, data: toUpdateRequest(currentJob, currentInitialForm, draft.form) });
    }, 1500);

    return () => clearTimeout(debounceTimerRef.current);
  }, [draft, jobId]);

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

    setLinksDraft({ jobId, linkedJobIds });

    const addedIds = linkedJobIds.filter(
      (id) => !existingLinkedIds.includes(id) && !pendingLinksRef.current.has(id),
    );
    const removedIds = existingLinkedIds.filter(
      (id) => !linkedJobIds.includes(id) && !pendingLinksRef.current.has(id),
    );

    if (addedIds.length === 0 && removedIds.length === 0) return;

    setLinksStatus('saving');

    addedIds.forEach((targetReportId) => {
      pendingLinksRef.current.add(targetReportId);
      linkMutation.mutate({ id: jobId, data: { targetReportId, linkType: 'related' } });
    });

    removedIds.forEach((targetReportId) => {
      const link = job.links.find((l) => l.linkedReportId === targetReportId);
      if (link) {
        pendingLinksRef.current.add(link.id);
        deleteLinkMutation.mutate({ id: jobId, linkId: link.id });
      }
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
