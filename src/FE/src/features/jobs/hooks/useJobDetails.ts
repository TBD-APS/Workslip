import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  getGetApiJobsQueryKey,
  getGetApiJobsIdQueryKey,
  useGetApiJobsId,
  usePostApiJobsIdAssign,
  usePatchApiJobsId,
} from '../../../api/generated/jobs/jobs';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { validateEmail, validatePhoneNumber } from '../../../components/forms/validators';
import type {
  CustomerInfo,
  JobReportSummaryViewModel,
  UpdateJobRequest,
} from '../../../api/generated/models';

export type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

export type JobDetailsForm = {
  customer: CustomerInfo;
  reportNumber: string;
  taskDescription: string;
  customerObservations: string;
};

export type AssignableUser = {
  id: string;
  displayName: string;
  email: string;
};

type JobDetailsDraft = {
  jobId: string;
  form: JobDetailsForm;
};

type AssignmentDraft = {
  jobId: string;
  userIds: string[];
};

const emptyCustomer: CustomerInfo = {
  customerId: null,
  name: null,
  address: null,
  email: null,
  contactPerson: null,
  phone: null,
};

export function useJobDetails(jobId: string | undefined) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<JobDetailsDraft | null>(null);
  const [currentStep, setCurrentStep] = useState(0);
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle');
  const [assignmentStatus, setAssignmentStatus] = useState<SaveStatus>('idle');
  const [assignmentDraft, setAssignmentDraft] = useState<AssignmentDraft | null>(null);
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const savedTimerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const assignmentTimerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const query = useGetApiJobsId(jobId ?? '', {
    query: {
      enabled: Boolean(jobId),
    },
  });
  const job = getResponseData<JobReportSummaryViewModel>(query.data);
  const usersQuery = useGetApiUsers();
  const assignableUsers = getUserList(usersQuery.data);
  const initialForm = job ? toForm(job) : null;
  const form = draft && draft.jobId === jobId ? draft.form : initialForm ?? emptyForm;
  const assignedUserIds = assignmentDraft && assignmentDraft.jobId === jobId
    ? assignmentDraft.userIds
    : job?.assignedUsers.map((user) => user.id) ?? [];

  const mutation = usePatchApiJobsId({
    mutation: {
      onSuccess: () => {
        if (jobId) {
          queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) });
          queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
        }
        setDraft(null);
        setSaveStatus('saved');
        clearTimeout(savedTimerRef.current);
        savedTimerRef.current = setTimeout(() => setSaveStatus('idle'), 2500);
      },
      onError: () => {
        setSaveStatus('error');
        toast.error('Kunne ikke gemme ændringer');
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
        clearTimeout(assignmentTimerRef.current);
        assignmentTimerRef.current = setTimeout(() => setAssignmentStatus('idle'), 2500);
      },
      onError: () => {
        setAssignmentStatus('error');
        toast.error('Kunne ikke opdatere tildeling');
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
  }, [draft, initialForm, job, jobId, mutation]);

  useEffect(() => {
    return () => {
      clearTimeout(debounceTimerRef.current);
      clearTimeout(savedTimerRef.current);
      clearTimeout(assignmentTimerRef.current);
    };
  }, []);

  const updateDraft = (nextForm: JobDetailsForm) => {
    if (!jobId) return;
    setDraft({ jobId, form: nextForm });
    if (saveStatus === 'saved') setSaveStatus('idle');
  };

  const updateCustomer = (field: keyof CustomerInfo, value: string | null) => {
    updateDraft({
      ...form,
      customer: {
        ...form.customer,
        [field]: toNullable(value),
      },
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

  return {
    job,
    form,
    assignableUsers,
    assignedUserIds,
    currentStep,
    setCurrentStep,
    isLoading: query.isLoading,
    isError: query.isError,
    isLoadingUsers: usersQuery.isLoading,
    saveStatus,
    assignmentStatus,
    reportNumberReadOnly: Boolean(job?.reportNumber),
    updateAssignedUsers,
    updateCustomer,
    updateReportNumber,
    updateTaskDescription,
    updateCustomerObservations,
  };
}

const emptyForm: JobDetailsForm = {
  customer: emptyCustomer,
  reportNumber: '',
  taskDescription: '',
  customerObservations: '',
};

function getResponseData<T>(value: T | { data: T } | { data: { data: T } } | undefined): T | undefined {
  if (!value) return undefined;
  if (!('data' in (value as object))) return value as T;

  const firstData = (value as { data: T | { data: T } }).data;
  if (firstData && typeof firstData === 'object' && 'data' in firstData) {
    return (firstData as { data: T }).data;
  }

  return firstData as T;
}

type UserViewModel = {
  id: string;
  displayName: string;
  email: string;
};

type UserListViewModel = {
  users: UserViewModel[];
};

function getUserList(value: unknown): AssignableUser[] {
  const data = getResponseData<UserListViewModel | UserViewModel[]>(value as UserListViewModel | UserViewModel[] | { data: UserListViewModel | UserViewModel[] } | undefined);
  const users = Array.isArray(data) ? data : data?.users ?? [];
  return users.map((user) => ({
    id: user.id,
    displayName: user.displayName,
    email: user.email,
  }));
}

function toForm(job: JobReportSummaryViewModel): JobDetailsForm {
  return {
    customer: {
      customerId: job.customer.customerId ?? null,
      name: job.customer.name ?? null,
      address: job.customer.address ?? null,
      email: job.customer.email ?? null,
      contactPerson: job.customer.contactPerson ?? null,
      phone: job.customer.phone ?? null,
    },
    reportNumber: job.reportNumber ?? '',
    taskDescription: job.observations.taskDescription ?? '',
    customerObservations: job.observations.customerObservations ?? '',
  };
}

function toUpdateRequest(job: JobReportSummaryViewModel, initial: JobDetailsForm, form: JobDetailsForm): UpdateJobRequest {
  const request: Partial<UpdateJobRequest> = {};

  if (!sameCustomer(initial.customer, form.customer)) {
    request.customer = form.customer;
  }

  if (!job.reportNumber && initial.reportNumber !== form.reportNumber) {
    request.reportNumber = form.reportNumber.trim() || null;
  }

  if (initial.taskDescription !== form.taskDescription || initial.customerObservations !== form.customerObservations) {
    request.observations = {
      reportDate: job.observations.reportDate ?? null,
      taskDescription: form.taskDescription.trim() || null,
      customerObservations: form.customerObservations.trim() || null,
      technicalObservations: job.observations.technicalObservations ?? null,
    };
  }

  return request as UpdateJobRequest;
}

function sameForm(left: JobDetailsForm, right: JobDetailsForm) {
  return JSON.stringify(left) === JSON.stringify(right);
}

function sameCustomer(left: CustomerInfo, right: CustomerInfo) {
  return left.customerId === right.customerId
    && left.name === right.name
    && left.address === right.address
    && left.email === right.email
    && left.contactPerson === right.contactPerson
    && left.phone === right.phone;
}

function isValidContactInfo(customer: CustomerInfo) {
  return validateEmail(customer.email) === null && validatePhoneNumber(customer.phone) === null;
}

function toNullable(value: string | null) {
  return value && value.length > 0 ? value : null;
}
