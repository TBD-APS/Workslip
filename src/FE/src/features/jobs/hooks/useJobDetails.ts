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
  sameWork,
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
type JobSaveMode = 'strict' | 'draft';

type SaveAllChangesOptions = {
  mode?: JobSaveMode;
  notifyOnSuccess?: boolean;
};

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
  // The PATCH currently in flight: the exact form object handed to it, whether
  // it carries the work slice, and a token identifying the request. All three
  // writers - flushSave, the debounced autosave and saveAllChanges - claim and
  // release this one slot, so "a write is in flight" has a single answer instead
  // of one per writer. It has to be a ref rather than `mutation.isPending`:
  // `isPending` is render-scoped, so two calls in the same tick both read it as
  // false and fired two PATCHes.
  const inFlightRequestRef = useRef<{ token: number; form: JobForm; sendsWork: boolean } | null>(null);
  const requestTokenRef = useRef(0);

  // Claim the slot for the request about to be issued. The token is what makes
  // the release attributable: only the settlement of this very request clears
  // it, so an unrelated response - an autosave landing, another writer's PATCH -
  // can no longer disarm a request that is still flying.
  //
  // Claiming immediately precedes every `mutate`/`mutateAsync` call, which is
  // what makes the slot safe without react-query's help: the slot always holds
  // the NEWEST request, and only that request's release can match. A request
  // superseded by a later one may never run its release - react-query drops a
  // per-call `mutateOptions` when a second `mutate` replaces the first - but its
  // token was overwritten before it could have matched anything, so the missing
  // release is a no-op either way.
  const beginRequest = useCallback((form: JobForm, sendsWork: boolean) => {
    requestTokenRef.current += 1;
    const token = requestTokenRef.current;
    inFlightRequestRef.current = { token, form, sendsWork };
    return token;
  }, []);

  const settleRequest = useCallback((token: number) => {
    if (inFlightRequestRef.current?.token === token) {
      inFlightRequestRef.current = null;
    }
  }, []);

  // Whether the PATCH in flight already covers this exact write. Form identity
  // is part of the answer, so every further edit - which always produces a new
  // form object - is sent instead of swallowed, and a slot that somehow never
  // settles can never block saving. A work-less request in flight must not stand
  // in for one that has to carry work, or the work payload is never sent at all.
  const isRequestInFlight = useCallback((form: JobForm, sendsWork: boolean) => {
    const inFlight = inFlightRequestRef.current;
    return inFlight !== null && inFlight.form === form && (inFlight.sendsWork || !sendsWork);
  }, []);

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
        } else if (currentDraft && !sameForm(newInitialForm, currentDraft.form)) {
          setDraft({
            jobId: currentDraft.jobId,
            form: {
              ...newInitialForm,
              work: currentDraft.form.work,
              editSnapshot: currentDraft.form.editSnapshot,
            },
          });
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
      // Only from the untouched first step: late-arriving reference data must not
      // teleport a user who has already navigated somewhere else.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setCurrentStep((step) => (step === 0 ? 3 : step));
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
        // Nothing left the client, so nothing failed - an 'error' chip here pins
        // "Fejl ved gem" over a form the user is still filling in.
        setSaveStatus('idle');
        return;
      }

      // The autosave never carries work, so a PATCH of this exact form already
      // in flight - with or without work - covers it.
      if (isRequestInFlight(draft.form, false)) {
        return;
      }

      setSaveStatus('saving');
      const token = beginRequest(draft.form, false);
      currentMutate(
        {
          id: jobId,
          data: toUpdateRequest(currentJob, currentInitialForm, draft.form, referenceData, { includeWork: false }),
        },
        { onSettled: () => settleRequest(token) },
      );
    }, 1500);

    return () => clearTimeout(debounceTimerRef.current);
  }, [autoSave, beginRequest, draft, isRequestInFlight, jobId, referenceData, setSaveStatus, settleRequest]);

  const updateDraft = useCallback((nextForm: JobForm) => {
    if (!jobId) return;
    setDraft({ jobId, form: nextForm });
    if (saveStatus === 'saved' || saveStatus === 'error') setSaveStatus('idle');
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
    if (saveStatus === 'saved' || saveStatus === 'error') setSaveStatus('idle');
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

  const updateAllIrrelevantReason = (value: string) => {
    updateDraft({
      ...form,
      work: { ...form.work, allIrrelevantReason: value },
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
    setLinksStatus('idle');

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
      // Never drop a slice this branch did not compare. With `includeWork` false
      // the work slice was left out of the comparison, so nulling the whole
      // draft silently deleted a pending anlægstype/opgavetype/kontrolpunkt edit
      // and reported the save as done - which is what every blocked-Næste and
      // locked-dot bounce does while the user stands on step 0. Rebase the
      // surviving work slice onto the persisted fields instead, the same shape
      // the mutation's onSuccess re-seed uses.
      if (!includeWork && !sameWork(initialForm, draft.form)) {
        setDraft({
          jobId: draft.jobId,
          form: {
            ...initialForm,
            work: draft.form.work,
            editSnapshot: draft.form.editSnapshot,
          },
        });
        return true;
      }
      setDraft(null);
      return true;
    }
    if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(job?.reportNumber), requireDestinationAddress: isAdmin })) {
      // Nothing left the client, so nothing failed - an 'error' chip here pins
      // "Fejl ved gem" on every validation bounce and every backward move. The
      // blocked Næste label and the validation summary already name the reason.
      setSaveStatus('idle');
      return false;
    }
    if (includeWork && validateWork && !isValidWork(draft.form, referenceData)) {
      // Nothing left the client, so nothing failed. This is the only refusal
      // that speaks, and it only runs when the caller asked for work to be
      // validated - which is exactly the callers whose move it blocks.
      setSaveStatus('idle');
      notify.error(getWorkValidationMessage(draft.form, referenceData) ?? 'Udfyld anlægstyper og opgavetype', {
        id: 'job-work-validation-error',
      });
      return false;
    }
    // Exactly the condition `toUpdateRequest` uses to fill the work slice, so
    // "does this PATCH carry work" is decided in one place. Whether that slice
    // can be serialised faithfully is not a question any writer has to ask:
    // `toWorkRequest` answers it per field - a null `installationTypes` leaves
    // the recorded ones alone - so no state withholds the write for ever.
    const sendsWork = includeWork && !sameWork(initialForm, draft.form);
    // A second tap while this exact write is already in flight must not issue a
    // second PATCH, but navigation still has to proceed - it is already saving.
    // The gates above run first, so 'already saving' can never stand in for
    // 'valid', and a work-less request in flight cannot stand in for one that
    // has to carry work.
    if (isRequestInFlight(draft.form, sendsWork)) {
      return true;
    }
    setSaveStatus('saving');
    const token = beginRequest(draft.form, sendsWork);
    mutation.mutate(
      {
        id: jobId,
        data: toUpdateRequest(job, initialForm, draft.form, referenceData, { includeWork }),
      },
      { onSettled: () => settleRequest(token) },
    );
    return true;
  };

  const saveAllChanges = async (options: SaveAllChangesOptions = {}) => {
    const mode = options.mode ?? 'strict';
    const notifySaveSuccess = () => {
      if (options.notifyOnSuccess) {
        notify.success('Ændringerne er gemt', { id: 'job-draft-save-success' });
      }
    };
    clearTimeout(debounceTimerRef.current);
    if (!draft || !initialForm || !job || !jobId) {
      if (saveStatus === 'saved') notifySaveSuccess();
      return true;
    }
    if (sameForm(initialForm, draft.form)) {
      setDraft(null);
      notifySaveSuccess();
      return true;
    }
    // Every refusal below is local: the toast names the reason and no request is
    // issued, so the status stays 'idle' - 'error' belongs to the mutation's own
    // onError and nowhere else.
    if (mode === 'strict') {
      if (!isValidJobForm(draft.form, { reportNumberReadOnly: Boolean(job?.reportNumber), requireDestinationAddress: isAdmin })) {
        setSaveStatus('idle');
        notify.error('Udfyld kundeoplysninger', { id: 'job-form-validation-error' });
        return false;
      }
      if (!isValidWork(draft.form, referenceData)) {
        setSaveStatus('idle');
        notify.error(getWorkValidationMessage(draft.form, referenceData) ?? 'Udfyld anlægstyper og opgavetype', {
          id: 'job-work-validation-error',
        });
        return false;
      }

      const cpValidation = validateControlPoints(draft.form, referenceData);
      if (!cpValidation.valid) {
        setSaveStatus('idle');
        notify.error(cpValidation.error ?? 'Udfyld venligst alle påkrævede kontrolpunkter', {
          id: 'job-cp-validation-error',
        });
        return false;
      }
    }

    // saveAllChanges always sends work when work changed. Nothing here can
    // withhold the write: `toWorkRequest` sends `installationTypes: null` when
    // the catalogue cannot resolve the selection, so the leave-save always gets
    // to issue its request and the navigation guard is never handed a `false`
    // it can do nothing about.
    const sendsWork = !sameWork(initialForm, draft.form);

    setSaveStatus('saving');
    const formBeingSaved = draft.form;
    // This writer awaits its own request, so it never short-circuits on the slot:
    // a leave-save must not report "saved" on the strength of someone else's
    // PATCH. It does claim the slot, so the debounced autosave and a flushSave
    // underneath it cannot fire a duplicate while it runs, and the `finally`
    // below releases it on success and on failure. A newer write that supersedes
    // this one takes the slot over in its own `beginRequest`, so this release
    // then finds a token that no longer matches and leaves it alone.
    const token = beginRequest(formBeingSaved, sendsWork);
    try {
      await mutation.mutateAsync({
        id: jobId,
        data: toUpdateRequest(job, initialForm, draft.form, referenceData, { includeWork: true }),
      });
      if (draftRef.current?.form !== formBeingSaved) {
        setSaveStatus('idle');
        return false;
      }
      notifySaveSuccess();
      return true;
    } catch {
      return false;
    } finally {
      settleRequest(token);
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
    const saved = flushSave({ includeWork, validateWork });
    // A backward move always lands. A refused save keeps the draft and names
    // itself where the user is looking - the work toast, or the blocked Næste
    // label and the validation summary - so going back loses nothing.
    if (saved || nextStep < currentStep) {
      setCurrentStep(nextStep);
      return true;
    }
    return false;
  };

  // Teleport straight to a step that holds a validation issue. The draft is
  // flushed first - work included whenever the user has been past step 0, and
  // from step 0 the pending anlægstype/opgavetype/kontrolpunkt edit survives in
  // the draft instead of being dropped by the no-op branch - but the move itself
  // is never gated on the save: landing on the offending field is the whole
  // point.
  //
  // And because the move is not gated on it, the flush must stay silent: the
  // caller has already shown the bounce toast that names the issue being jumped
  // to, and a second toast about a save the wizard has already moved past is
  // noise the user cannot act on. `validateWork: false` is what buys that -
  // flushSave's only local toast sits behind it, precisely so a caller that
  // refuses nothing says nothing.
  const jumpToStep = (step: number) => {
    flushSave({ includeWork: currentStep >= 1, validateWork: false });
    setCurrentStep(step);
  };

  // Returns true only when the wizard actually moved, so a caller does not park
  // keyboard focus on a step region the user never left.
  //
  // No step gate of its own. Reachability is decided by ONE range - the steps a
  // click has to walk, `[currentStep, nextStep)` in JobDetails'
  // `findBlockingIssue`, which also styles the dot, names it in Danish and
  // fires the bounce; the Næste button is gated on the current step's own
  // issues. A second gate here could only ever check a step outside that range
  // (step-0 validity from step 2, say) and would then refuse a move the dots
  // showed as open, with a toast naming no step. What still blocks a forward
  // move is the save itself: `saveCurrentStepAndSetCurrentStep` only advances
  // when flushSave got the pending draft out, and flushSave keeps its own
  // payload-level gates.
  const navigateToStep = (nextStep: number): boolean => {
    if (nextStep === currentStep) return false;

    const moved = saveCurrentStepAndSetCurrentStep(nextStep);
    document.querySelector('.app-shell')?.scrollTo(0, 0);
    return moved;
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
    jumpToStep,
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
    updateAllIrrelevantReason,
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
