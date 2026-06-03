import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { AxiosError } from 'axios';
import { toast } from 'sonner';
import {
  getGetApiJobsQueryKey,
  getGetApiJobsIdQueryKey,
  useDeleteApiJobsIdLinks,
  useGetApiJobsId,
  usePostApiJobsIdAssign,
  usePostApiJobsIdLinks,
  usePatchApiJobsId,
} from '../../../api/generated/jobs/jobs';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { useGetApiReferenceData } from '../../../api/generated/reference-data/reference-data';
import { useTimedStatus } from '../../../hooks/useTimedStatus';
import {
  emptyForm,
  getWorkValidationMessage,
  getLinkableJobs,
  getResponseData,
  getUserList,
  isValidJobForm,
  isValidWork,
  sameForm,
  sameFormWithoutWork,
  sameWork,
  toForm,
  toNullable,
  toUpdateRequest,
} from '../utils';
import type { CustomerInfo, JobReportSummaryViewModel } from '../../../api/generated/models';
import type { JobForm, ReferenceData } from '../types';

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
  const draftRef = useRef<JobDetailsDraft | null>(null);

  const query = useGetApiJobsId(jobId ?? '', {
    query: { enabled: Boolean(jobId) },
  });
  
  const job = getResponseData<JobReportSummaryViewModel>(query.data);
  const usersQuery = useGetApiUsers();
  const referenceDataQuery = useGetApiReferenceData();
  const jobsData = queryClient.getQueryData(getGetApiJobsQueryKey({ limit: 200 }));
  const assignableUsers = getUserList(usersQuery.data);
  const referenceData = getResponseData<ReferenceData>(
    referenceDataQuery.data as ReferenceData | { data: ReferenceData } | { data: { data: ReferenceData } } | undefined,
  ) ?? null;

  const linkableJobs = getLinkableJobs(jobsData, jobId);
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
      onSuccess: (_data, variables) => {
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
        }
        const currentDraft = draftRef.current;
        const currentInitialForm = initialFormRef.current;
        if (
          variables.data.work === null &&
          currentDraft &&
          currentInitialForm &&
          !sameWork(currentInitialForm, currentDraft.form)
        ) {
          setDraft(currentDraft);
        } else {
          setDraft(null);
        }
        setSaveStatus('saved');
      },
      onError: (error) => {
        setSaveStatus('error');
        toast.error(getSaveErrorMessage(error), { id: 'job-save-error' });
      },
    },
  });

  const initialFormRef = useRef(initialForm);
  const jobRef = useRef(job);
  const mutateRef = useRef(mutation.mutate);
  draftRef.current = draft;
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
        for (const id of variables.data.targetReportIds) {
          pendingLinksRef.current.delete(id);
        }
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
        }
        if (pendingLinksRef.current.size === 0) {
          setLinksStatus('saved');
        }
      },
      onError: (_error, variables) => {
        for (const id of variables.data.targetReportIds) {
          pendingLinksRef.current.delete(id);
        }
        setLinksStatus('error');
        toast.error('Kunne ikke opdatere tilknyttede sager', { id: 'job-links-error' });
      },
    },
  });

  const deleteLinkMutation = useDeleteApiJobsIdLinks({
    mutation: {
      onSuccess: (_data, variables) => {
        for (const id of variables.data.linkIds) {
          pendingLinksRef.current.delete(id);
        }
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
        }
        if (pendingLinksRef.current.size === 0) {
          setLinksStatus('saved');
        }
      },
      onError: (_error, variables) => {
        for (const id of variables.data.linkIds) {
          pendingLinksRef.current.delete(id);
        }
        setLinksStatus('error');
        toast.error('Kunne ikke fjerne tilknyttede sager', { id: 'job-links-error' });
      },
    },
  });

  useEffect(() => {
    const currentInitialForm = initialFormRef.current;
    const currentJob = jobRef.current;
    const currentMutate = mutateRef.current;
    if (!draft || !currentInitialForm || !currentJob || !jobId) return;

    if (sameFormWithoutWork(currentInitialForm, draft.form)) {
      return;
    }

    debounceTimerRef.current = setTimeout(() => {
      if (sameFormWithoutWork(currentInitialForm, draft.form)) {
        return;
      }

      if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(job?.reportNumber) })) {
        setSaveStatus('error');
        return;
      }

      setSaveStatus('saving');
      currentMutate({
        id: jobId,
        data: toUpdateRequest(currentJob, currentInitialForm, draft.form, referenceData, { includeWork: false }),
      });
    }, 1500);

    return () => clearTimeout(debounceTimerRef.current);
  }, [draft, jobId, referenceData]);

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

  const updateWorkCategories = (categoryIds: string[]) => {
    updateDraft({ ...form, work: { ...form.work, categoryIds, controlPointSelections: {}, irrelevantCategoryIds: [] } });
  };

  const updateWorkKind = (workKind: string) => {
    const selectedWorkKind = referenceData?.workKinds.find((kind) => kind.normalizedLabel === workKind);
    updateDraft({
      ...form,
      work: {
        ...form.work,
        workKind,
        customWorkKind: selectedWorkKind?.requiresCustomWorkKind ? form.work.customWorkKind : '',
      },
    });
  };

  const updateCustomWorkKind = (customWorkKind: string) => {
    updateDraft({ ...form, work: { ...form.work, customWorkKind } });
  };

  const toggleControlPoint = (cpId: string) => {
    updateDraft({
      ...form,
      work: {
        ...form.work,
        controlPointSelections: {
          ...form.work.controlPointSelections,
          [cpId]: !form.work.controlPointSelections[cpId],
        },
      },
    });
  };

  const toggleCategoryIrrelevant = (categoryId: string) => {
    const isIrrelevant = form.work.irrelevantCategoryIds.includes(categoryId);
    const irrelevantCategoryIds = isIrrelevant
      ? form.work.irrelevantCategoryIds.filter((id) => id !== categoryId)
      : [...form.work.irrelevantCategoryIds, categoryId];

    updateDraft({
      ...form,
      work: { ...form.work, irrelevantCategoryIds },
    });
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
    const removedLinks = job.links.filter(
      (link) => !linkedJobIds.includes(link.linkedReportId) && !pendingLinksRef.current.has(link.id),
    );

    if (addedIds.length === 0 && removedLinks.length === 0) return;

    setLinksStatus('saving');

    if (addedIds.length > 0) {
      for (const id of addedIds) {
        pendingLinksRef.current.add(id);
      }
      linkMutation.mutate({ id: jobId, data: { targetReportIds: addedIds } });
    }

    if (removedLinks.length > 0) {
      const linkIds = removedLinks.map((link) => link.id);
      for (const id of linkIds) {
        pendingLinksRef.current.add(id);
      }
      deleteLinkMutation.mutate({ id: jobId, data: { linkIds } });
    }
  };

  const flushSave = (options: { includeWork?: boolean; validateWork?: boolean } = {}) => {
    const includeWork = options.includeWork ?? false;
    const validateWork = options.validateWork ?? false;
    clearTimeout(debounceTimerRef.current);
    if (!draft || !initialForm || !job || !jobId) return true;
    if (includeWork ? sameForm(initialForm, draft.form) : sameFormWithoutWork(initialForm, draft.form)) {
      setDraft(null);
      return true;
    }
    if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(job?.reportNumber) })) {
      setSaveStatus('error');
      return false;
    }
    if (includeWork && validateWork && !isValidWork(draft.form, referenceData)) {
      setSaveStatus('error');
      toast.error(getWorkValidationMessage(draft.form, referenceData) ?? 'Udfyld kategorier og arbejdstype', {
        id: 'job-work-validation-error',
      });
      return false;
    }
    setSaveStatus('saving');
    mutation.mutate({
      id: jobId,
      data: toUpdateRequest(job, initialForm, draft.form, referenceData, { includeWork }),
    });
    return true;
  };

  const saveCurrentStep = (options: { validateWork?: boolean } = {}) => flushSave({
    includeWork: currentStep >= 1,
    validateWork: options.validateWork ?? false,
  });

  const saveCurrentStepAndSetCurrentStep = (nextStep: number) => {
    const includeWork = currentStep >= 1;
    const validateWork = includeWork && nextStep > currentStep;
    if (flushSave({ includeWork, validateWork })) {
      setCurrentStep(nextStep);
    }
  };

  const navigateToStep = (nextStep: number) => {
    if (nextStep === currentStep) return;

    if (nextStep > currentStep) {
      if (!isValidJobForm(form, { reportNumberReadOnly: Boolean(job?.reportNumber) })) {
        setSaveStatus('error');
        toast.error('Udfyld kundeoplysninger', { id: 'job-form-validation-error' });
        return;
      }

      if (nextStep > 1 && !isValidWork(form, referenceData)) {
        setSaveStatus('error');
        toast.error(getWorkValidationMessage(form, referenceData) ?? 'Udfyld kategorier og arbejdstype', {
          id: 'job-work-validation-error',
        });
        return;
      }
    }

    saveCurrentStepAndSetCurrentStep(nextStep);
  };

  return {
    job,
    form,
    referenceData,
    assignableUsers,
    assignedUserIds,
    linkableJobs,
    linkedJobIds,
    currentStep,
    setCurrentStep,
    isLoading: query.isLoading,
    isError: query.isError,
    isLoadingUsers: usersQuery.isLoading,
    isLoadingReferenceData: referenceDataQuery.isLoading,
    isLoadingJobs: false,
    saveStatus,
    assignmentStatus,
    linksStatus,
    canContinue: isValidJobForm(form, { reportNumberReadOnly: Boolean(job?.reportNumber) }) && isValidWork(form, referenceData),
    reportNumberReadOnly: Boolean(job?.reportNumber),
    flushSave,
    saveCurrentStep,
    saveCurrentStepAndSetCurrentStep,
    navigateToStep,
    updateAssignedUsers,
    updateLinkedJobs,
    updateCustomer,
    updateReportNumber,
    updateTaskDescription,
    updateCustomerObservations,
    updateWorkCategories,
    updateWorkKind,
    updateCustomWorkKind,
    toggleControlPoint,
    toggleCategoryIrrelevant,
  };
}

function getSaveErrorMessage(error: unknown) {
  const axiosError = error as AxiosError<{ error?: string }>;
  if (axiosError.response?.status === 409 && axiosError.response.data?.error === 'duplicate_report_number') {
    return 'Sagsnummeret findes allerede.';
  }

  return 'Kunne ikke gemme ændringer';
}
