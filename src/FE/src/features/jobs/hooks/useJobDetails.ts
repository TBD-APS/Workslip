import { useCallback, useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { AxiosError } from 'axios';
import { notify } from '../../../lib/toast';
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
import type { JobListItemViewModel } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models';
import { apiClient } from '../../../lib/axios';
import {
  useDeleteApiWorksheetsWorksheetIdJobsJobId,
  usePostApiWorksheetsJobsJobId,
} from '../../../api/generated/worksheet/worksheet';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { useGetApiReferenceData } from '../../../api/generated/reference-data/reference-data';
import { useTimedStatus } from '../../../hooks/useTimedStatus';
import { canReceiveJobAssignment, useIsAdmin } from '../../../providers/permissions';
import { useAuth } from '../../../providers/useAuth';
import {
  emptyForm,
  getWorkValidationMessage,
  getLinkableJobs,
  isValidJobForm,
  isValidWork,
  sameForm,
  sameFormWithoutWork,
  toForm,
  toUpdateRequest,
} from '../utils';
import { validateControlPoints } from '../components/steps/controlPointsValidation';
import type { JobForm } from '../types';
import type { CustomerSnapshotData } from '../../../api/generated/models/customerSnapshotData';
import { useCustomerSnapshot } from './useCustomerSnapshot';

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
  const { user } = useAuth();
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
  const autoRedirectDoneRef = useRef(false);

  const query = useGetApiJobsId(jobId ?? '', {
    query: { enabled: Boolean(jobId) },
  });
  
  const job = query.data;
  const usersQuery = useGetApiUsers({ limit: 200 }, { query: { enabled: isAdmin } });
  const referenceDataQuery = useGetApiReferenceData();
  const jobsQuery = useQuery({
    queryKey: getGetApiJobsQueryKey({ status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview], limit: 200 }),
    queryFn: async () => {
      const data = await apiClient.get('/api/jobs', { params: { status: [JobStatus.Draft, JobStatus.Approved, JobStatus.InReview], limit: 200 } }) as { items: JobListItemViewModel[]; totalCount: number };
      return data.items;
    },
  });
  const assignableUsers = (usersQuery.data?.users ?? []).filter((candidate) => canReceiveJobAssignment(candidate.role));
  const referenceData = referenceDataQuery.data!;

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
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        const newInitialForm = toForm(data);
        initialFormRef.current = newInitialForm;
        const currentDraft = draftRef.current;
        if (currentDraft && !sameFormWithoutWork(newInitialForm, currentDraft.form)) {
          setDraft(currentDraft);
        } else if (currentDraft?.form.editSnapshot) {
          setDraft({
            jobId: currentDraft.jobId,
            form: {
              ...newInitialForm,
              editSnapshot: true,
            },
          });
        } else {
          setDraft(null);
        }
        setSaveStatus('saved');
      },
      onError: (error) => {
        setSaveStatus('error');
        notify.error(getSaveErrorMessage(error), { id: 'job-save-error' });
      },
    },
    request: { skipGlobalErrorToast: true },
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

  // Auto-redirect to worksheets step if user is assigned, has a worksheet, and all prior steps are complete
  useEffect(() => {
    if (!job || !referenceData || !user || autoRedirectDoneRef.current) return;
    if (job.status === JobStatus.Rejected) return;

    const isAssigned = job.assignedUsers.some((u) => u.id === user.id);
    const hasWorksheet = job.worksheets.some((ws) => ws.userId === user.id);

    if (!isAssigned || !hasWorksheet) return;

    const form = toForm(job);
    const jobFormValid = isValidJobForm(form, { reportNumberReadOnly: Boolean(job.reportNumber), requireDestinationAddress: isAdmin });
    const workValid = isValidWork(form, referenceData);
    const controlPointsValid = validateControlPoints(form, referenceData).valid;

    if (jobFormValid && workValid && controlPointsValid) {
      autoRedirectDoneRef.current = true;
      // Auto-navigation intentionally follows asynchronous job/reference-data resolution.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setCurrentStep(3);
    }
  }, [job, referenceData, user, isAdmin]);

  const assignmentMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
        setAssignmentStatus('saved');
      },
      onError: () => {
        setAssignmentStatus('error');
        notify.error('Kunne ikke opdatere tildeling', { id: 'job-assign-error' });
      },
    },
    request: { skipGlobalErrorToast: true },
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
        queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
        if (pendingLinksRef.current.size === 0) {
          setLinksStatus('saved');
        }
      },
      onError: (_error, variables) => {
        for (const id of variables.data.targetReportIds) {
          pendingLinksRef.current.delete(id);
        }
        setLinksStatus('error');
        notify.error('Kunne ikke opdatere tilknyttede sager', { id: 'job-links-error' });
      },
    },
    request: { skipGlobalErrorToast: true },
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
        notify.error('Kunne ikke fjerne tilknyttede sager', { id: 'job-links-error' });
      },
    },
    request: { skipGlobalErrorToast: true },
  });

  const upsertWorksheetMutation = usePostApiWorksheetsJobsJobId({
    mutation: {
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        queryClient.invalidateQueries({ queryKey: ['worksheets'] });
        notify.success('Arbejdssedlen er gemt');
      },
      onError: (error) => {
        notify.error(getWorksheetErrorMessage(error), { id: 'worksheet-upsert-error' });
      },
    },
    request: { skipGlobalErrorToast: true },
  });

  const deleteWorksheetMutation = useDeleteApiWorksheetsWorksheetIdJobsJobId({
    mutation: {
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        queryClient.invalidateQueries({ queryKey: ['worksheets'] });
        notify.success('Arbejdssedlen er slettet');
      },
      onError: (error) => {
        notify.error(getWorksheetDeleteErrorMessage(error), { id: 'worksheet-delete-error' });
      },
    },
    request: { skipGlobalErrorToast: true },
  });

  const submitJobMutation = usePostApiJobsIdStatus({
    mutation: {
      onSuccess: (data) => {
        if (jobId) {
          queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), data);
        }
        queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
        const sagsnummer = data.reportNumber ?? '';
        notify.success(`Sagen SAG-${sagsnummer.toUpperCase()} er attesteret og indsendt`);
      },
      onError: (error) => {
        notify.error(getSubmitErrorMessage(error), { id: 'job-submit-error' });
      },
    },
    request: { skipGlobalErrorToast: true },
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

      if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(currentJob.reportNumber), requireDestinationAddress: isAdmin })) {
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

  const updateDraft = useCallback((nextForm: JobForm) => {
    if (!jobId) return;
    setDraft({ jobId, form: nextForm });
    if (saveStatus === 'saved') setSaveStatus('idle');
  }, [jobId, saveStatus, setDraft, setSaveStatus]);

  // Functional form update: derives the current form from the previous
  // draft (or the loaded initial form) so sequential updates in the same
  // tick compose instead of clobbering each other. Mirrors useJobCreate's
  // setForm((prev) => ...) pattern.
  const updateForm = useCallback((updater: (prev: JobForm) => JobForm) => {
    if (!jobId) return;
    setDraft((prev) => {
      const base = prev && prev.jobId === jobId ? prev.form : (initialFormRef.current ?? emptyForm);
      return { jobId, form: updater(base) };
    });
    if (saveStatus === 'saved') setSaveStatus('idle');
  }, [jobId, saveStatus, setDraft, setSaveStatus]);

  // Adapter: useCustomerSnapshot expects a setter that takes an
  // updater fn and returns the next slice. useJobDetails's `updateDraft`
  // takes a fully-formed form. Bridge them so the snapshot logic
  // stays shared with useJobCreate.
  const setCustomerForm = useCallback(
    <S extends {
      customerId: string | null;
      customerSnapshot: CustomerSnapshotData | null;
      editSnapshot: boolean;
      createCustomer: boolean;
    }>(
      updater: (prev: S) => S,
    ) => updateDraft(updater(form as unknown as S) as unknown as JobForm),
    [form, updateDraft],
  );

  const { selectCustomer, updateSnapshotField, updateEditSnapshot } = useCustomerSnapshot(setCustomerForm);

  const updateDestinationAddress = (value: string) => {
    updateForm((prev) => ({ ...prev, destinationAddress: value }));
  };

  const updateCreateCustomer = (value: boolean) => {
    updateForm((prev) => ({ ...prev, createCustomer: value }));
  };

  const updateDestinationZipCode = (value: string) => {
    updateForm((prev) => ({ ...prev, destinationZipCode: value }));
  };

  const updateDestinationCity = (value: string) => {
    updateForm((prev) => ({ ...prev, destinationCity: value }));
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
    return submitJobMutation.mutateAsync({ id: jobId, data: { status: JobStatus.InReview } });
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
    if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(job?.reportNumber), requireDestinationAddress: isAdmin })) {
      setSaveStatus('error');
      return false;
    }
    if (includeWork && validateWork && !isValidWork(draft.form, referenceData)) {
      setSaveStatus('error');
      notify.error(getWorkValidationMessage(draft.form, referenceData) ?? 'Udfyld anlægstyper og opgavetype', {
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
    if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(job?.reportNumber), requireDestinationAddress: isAdmin })) {
      setSaveStatus('error');
      notify.error('Udfyld kundeoplysninger', { id: 'job-form-validation-error' });
      return false;
    }
    if (!isValidWork(draft.form, referenceData)) {
      setSaveStatus('error');
      notify.error(getWorkValidationMessage(draft.form, referenceData) ?? 'Udfyld anlægstyper og opgavetype', {
        id: 'job-work-validation-error',
      });
      return false;
    }

    const cpValidation = validateControlPoints(draft.form, referenceData);
    if (!cpValidation.valid) {
      setSaveStatus('error');
      notify.error(cpValidation.error ?? 'Udfyld venligst alle påkrævede kontrolpunkter', {
        id: 'job-cp-validation-error',
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
      if (!isValidJobForm(form, { reportNumberReadOnly: Boolean(job?.reportNumber), requireDestinationAddress: isAdmin })) {
        setSaveStatus('error');
        notify.error('Udfyld kundeoplysninger', { id: 'job-form-validation-error' });
        return;
      }

      if (nextStep > 1 && !isValidWork(form, referenceData)) {
        setSaveStatus('error');
        notify.error(getWorkValidationMessage(form, referenceData) ?? 'Udfyld anlægstyper og opgavetype', {
          id: 'job-work-validation-error',
        });
        return;
      }
    }

    saveCurrentStepAndSetCurrentStep(nextStep);
    document.querySelector('.app-shell')?.scrollTo(0, 0);
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
    canContinue: isValidJobForm(form, { reportNumberReadOnly: Boolean(job?.reportNumber), requireDestinationAddress: isAdmin }) && isValidWork(form, referenceData),
    hasUnsavedChanges: draft !== null && initialForm !== null && !sameForm(initialForm, draft.form),
    isAdmin,
    reportNumberReadOnly: Boolean(job?.reportNumber),
    flushSave,
    saveAllChanges,
    discardChanges,
    saveCurrentStep,
    saveCurrentStepAndSetCurrentStep,
    navigateToStep,
    updateAssignedUsers,
    updateLinkedJobs,
    selectCustomer,
    updateSnapshotField,
    updateEditSnapshot,
    updateCreateCustomer,
    updateDestinationAddress,
    updateDestinationZipCode,
    updateDestinationCity,
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
