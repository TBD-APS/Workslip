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
  usePostApiJobsIdStatus,
  usePatchApiJobsId,
} from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import {
  useDeleteApiWorksheetsWorksheetIdJobsJobId,
  usePostApiWorksheetsJobsJobId,
} from '../../../api/generated/worksheet/worksheet';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { useGetApiReferenceData } from '../../../api/generated/reference-data/reference-data';
import { useTimedStatus } from '../../../hooks/useTimedStatus';
import { useIsAdmin } from '../../../providers/permissions';
import {
  emptyForm,
  getWorkValidationMessage,
  getLinkableJobs,
  isValidJobForm,
  isValidWork,
  sameForm,
  sameFormWithoutWork,
  toForm,
  toNullable,
  toUpdateRequest,
} from '../utils';
import type { CustomerInfo } from '../../../api/generated/models';
import type { JobForm } from '../types';

type JobDetailsDraft = { jobId: string; form: JobForm };
type AssignmentDraft = { jobId: string; userIds: string[] };
type LinksDraft = { jobId: string; linkedJobIds: string[] };

export function useJobDetails(jobId: string | undefined) {
  return useJobDetailsState(jobId);
}

export function useJobDetailsState(jobId: string | undefined, options: { autoSave?: boolean } = {}) {
  const autoSave = options.autoSave ?? true;
  const queryClient = useQueryClient();
  const isAdmin = useIsAdmin();
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
  
  const job = query.data;
  const usersQuery = useGetApiUsers({ query: { enabled: isAdmin } });
  const referenceDataQuery = useGetApiReferenceData();
  const jobsData = queryClient.getQueryData(getGetApiJobsQueryKey({ limit: 200 }));
  const assignableUsers = usersQuery.data?.users ??  null;
  const referenceData = referenceDataQuery.data ?? null;

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
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        const currentDraft = draftRef.current;
        const newInitialForm = toForm(data);
        if (currentDraft && !sameFormWithoutWork(newInitialForm, currentDraft.form)) {
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

  useEffect(() => {
    draftRef.current = draft;
    initialFormRef.current = initialForm;
    jobRef.current = job;
    mutateRef.current = mutation.mutate;
  }, [draft, initialForm, job, mutation.mutate]);

  const assignmentMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
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

  const upsertWorksheetMutation = usePostApiWorksheetsJobsJobId({
    mutation: {
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        toast.success('Arbejdssedlen er gemt');
      },
      onError: (error) => {
        toast.error(getWorksheetErrorMessage(error), { id: 'worksheet-upsert-error' });
      },
    },
  });

  const deleteWorksheetMutation = useDeleteApiWorksheetsWorksheetIdJobsJobId({
    mutation: {
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        toast.success('Arbejdssedlen er slettet');
      },
      onError: (error) => {
        toast.error(getWorksheetDeleteErrorMessage(error), { id: 'worksheet-delete-error' });
      },
    },
  });

  const submitJobMutation = usePostApiJobsIdStatus({
    mutation: {
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey({ limit: 200 }) });
        toast.success('Sagen er attesteret og indsendt');
      },
      onError: (error) => {
        toast.error(getSubmitErrorMessage(error), { id: 'job-submit-error' });
      },
    },
  });

  useEffect(() => {
    if (!autoSave) return;
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

      if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(currentJob.reportNumber) })) {
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
  }, [autoSave, draft, jobId, referenceData, setSaveStatus]);

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

  const updateTechnicalObservations = (value: string) => {
    updateDraft({ ...form, technicalObservations: value });
  };

  const updateWorkCategories = (categoryIds: string[]) => {
    updateDraft({ ...form, work: { ...form.work, categoryIds } });
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

  const updateClosureFlags = (closureFlags: string[]) => {
    const nextForm = {
      ...form,
      work: { ...form.work, closureFlags },
    };
    updateDraft(nextForm);
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

  const toggleCategoryIrrelevant = (typeId: string, categoryId: string) => {
    const compositeId = `${typeId}-${categoryId}`;
    const isIrrelevant = form.work.irrelevantCategoryIds.includes(compositeId);
    const irrelevantCategoryIds = isIrrelevant
      ? form.work.irrelevantCategoryIds.filter((id) => id !== compositeId)
      : [...form.work.irrelevantCategoryIds, compositeId];

    let controlPointSelections = form.work.controlPointSelections;
    if (!isIrrelevant && referenceData) {
      const installationType = referenceData.installationTypes.find((t) => t.id === typeId);
      const category = installationType?.categories.find((c) => c.id === categoryId);
      if (category) {
        controlPointSelections = { ...form.work.controlPointSelections };
        for (const cp of category.controlPoints) {
          delete controlPointSelections[cp.id];
        }
      }
    }

    updateDraft({
      ...form,
      work: { ...form.work, irrelevantCategoryIds, controlPointSelections },
    });
  };

  const updateAssignedUsers = (userIds: string[]) => {
    if (!jobId || !isAdmin) return;
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

  const upsertWorksheet = (params: {
    id?: string;
    jobId: string;
    userId: string;
    userDisplayName: string;
    workDate: string;
    hoursWorked: number;
    sleptOnJob: boolean;
  }) => {
    return upsertWorksheetMutation.mutateAsync({
      jobId: params.jobId,
      data: {
        id: params.id ?? null,
        jobId: params.jobId,
        userId: params.userId,
        userDisplayName: params.userDisplayName,
        workDate: params.workDate,
        hoursWorked: params.hoursWorked,
        sleptOnJob: params.sleptOnJob,
      },
    });
  };

  const deleteWorksheet = (params: { worksheetId: string; jobId: string }) => {
    deleteWorksheetMutation.mutate({
      worksheetId: params.worksheetId,
      jobId: params.jobId,
    });
  };

  const submitJob = () => {
    if (!jobId) return Promise.resolve();
    return submitJobMutation.mutateAsync({ id: jobId, data: { status: JobStatus.Submitted } });
  };

  const submitJobFieldErrors = getSubmitFieldErrors(submitJobMutation.error);

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
      toast.error(getWorkValidationMessage(draft.form, referenceData) ?? 'Udfyld anlægstyper og opgavetype', {
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

  const saveAllChanges = async () => {
    clearTimeout(debounceTimerRef.current);
    if (!draft || !initialForm || !job || !jobId) return true;
    if (sameForm(initialForm, draft.form)) {
      setDraft(null);
      return true;
    }
    if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(job?.reportNumber) })) {
      setSaveStatus('error');
      toast.error('Udfyld kundeoplysninger', { id: 'job-form-validation-error' });
      return false;
    }
    if (!isValidWork(draft.form, referenceData)) {
      setSaveStatus('error');
      toast.error(getWorkValidationMessage(draft.form, referenceData) ?? 'Udfyld anlægstyper og opgavetype', {
        id: 'job-work-validation-error',
      });
      return false;
    }

    setSaveStatus('saving');
    try {
      await mutation.mutateAsync({
        id: jobId,
        data: toUpdateRequest(job, initialForm, draft.form, referenceData, { includeWork: true }),
      });
      return true;
    } catch {
      return false;
    }
  };

  const discardChanges = () => {
    clearTimeout(debounceTimerRef.current);
    setDraft(null);
    setSaveStatus('idle');
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
        toast.error(getWorkValidationMessage(form, referenceData) ?? 'Udfyld anlægstyper og opgavetype', {
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
    worksheets: job?.worksheets ?? [],
    currentStep,
    setCurrentStep,
    isLoading: query.isLoading,
    isError: query.isError,
    refetch: query.refetch,
    isLoadingUsers: usersQuery.isLoading,
    isLoadingReferenceData: referenceDataQuery.isLoading,
    isLoadingJobs: false,
    saveStatus,
    assignmentStatus,
    linksStatus,
    canContinue: isValidJobForm(form, { reportNumberReadOnly: Boolean(job?.reportNumber) }) && isValidWork(form, referenceData),
    reportNumberReadOnly: Boolean(job?.reportNumber),
    flushSave,
    saveAllChanges,
    discardChanges,
    saveCurrentStep,
    saveCurrentStepAndSetCurrentStep,
    navigateToStep,
    updateAssignedUsers,
    updateLinkedJobs,
    updateCustomer,
    updateReportNumber,
    updateTaskDescription,
    updateCustomerObservations,
    updateTechnicalObservations,
    updateWorkCategories,
    updateWorkKind,
    updateCustomWorkKind,
    updateClosureFlags,
    toggleControlPoint,
    toggleCategoryIrrelevant,
    upsertWorksheet,
    deleteWorksheet,
    submitJob,
    submitJobFieldErrors,
    isSavingWorksheet: upsertWorksheetMutation.isPending,
    isDeletingWorksheet: deleteWorksheetMutation.isPending,
    isSubmittingJob: submitJobMutation.isPending,
  };
}

function getSaveErrorMessage(error: unknown) {
  const axiosError = error as AxiosError<{ error?: string }>;
  if (axiosError.response?.status === 409 && axiosError.response.data?.error === 'duplicate_report_number') {
    return 'Sagsnummeret findes allerede.';
  }

  return 'Kunne ikke gemme ændringer';
}

function getWorksheetErrorMessage(error: unknown) {
  const axiosError = error as AxiosError<{ error?: string; message?: string }>;
  if (axiosError.response?.status === 400) {
    return 'Kontrollér montør, dato og timer.';
  }
  if (axiosError.response?.status === 409) {
    const errorText = axiosError.response.data?.error ?? axiosError.response.data?.message;
    if (errorText?.includes('24')) {
      return 'Montøren kan ikke registrere mere end 24 timer på samme dato.';
    }
    if (errorText?.includes('not found')) {
      return 'Arbejdssedlen findes ikke længere.';
    }
    return 'Arbejdssedlen kunne ikke gemmes.';
  }
  return 'Kunne ikke gemme arbejdssedlen';
}

function getWorksheetDeleteErrorMessage(error: unknown) {
  const axiosError = error as AxiosError<{ error?: string }>;
  if (axiosError.response?.status === 404) {
    return 'Arbejdssedlen findes ikke længere';
  }
  if (axiosError.response?.status === 409) {
    return 'Arbejdssedlen kunne ikke slettes — status forhindrer ændringer';
  }
  return 'Kunne ikke slette arbejdssedlen';
}

type ValidationProblem = {
  title?: string;
  errors?: Record<string, string[]>;
};

function getSubmitErrorMessage(error: unknown) {
  const fieldErrors = getSubmitFieldErrors(error);
  if (fieldErrors.length > 0) {
    return 'Sagen kan ikke attesteres endnu';
  }

  const axiosError = error as AxiosError<{ error?: string; message?: string }>;
  if (axiosError.response?.status === 409) {
    return 'Sagen kunne ikke attesteres — status forhindrer ændringen';
  }
  if (axiosError.response?.status === 404) {
    return 'Sagen findes ikke længere';
  }

  return 'Kunne ikke attestere sagen';
}

function getSubmitFieldErrors(error: unknown) {
  if (!error || typeof error !== 'object') return [];

  const axiosError = error as AxiosError<ValidationProblem>;
  const errors = axiosError.response?.data?.errors;
  if (!errors) return [];

  return Object.entries(errors).flatMap(([field, messages]) =>
    messages.map((message) => ({ field, message })),
  );
}
