import type {
  CustomerInfo,
  JobReportSummaryViewModel,
  UpdateJobRequest,
} from '../../api/generated/models';
import { validateEmail, validatePhoneNumber } from '../../components/forms/validators';
import type { AssignableUser, JobForm, LinkableJob } from './types';

type UserViewModel = { id: string; displayName: string; email: string };
type UserListViewModel = { users: UserViewModel[] };
type JobListItemViewModel = {
  id: string;
  reportNumber: string | null;
  customer: CustomerInfo | null;
  status: string;
};

export const emptyCustomer: CustomerInfo = {
  customerId: null,
  name: null,
  address: null,
  email: null,
  contactPerson: null,
  phone: null,
};

export const emptyForm: JobForm = {
  customer: { ...emptyCustomer },
  reportNumber: '',
  taskDescription: '',
  customerObservations: '',
};

export function getResponseData<T>(
  value: T | { data: T } | { data: { data: T } } | undefined,
): T | undefined {
  if (!value) return undefined;
  if (!('data' in (value as object))) return value as T;

  const firstData = (value as { data: T | { data: T } }).data;
  if (firstData && typeof firstData === 'object' && 'data' in firstData) {
    return (firstData as { data: T }).data;
  }

  return firstData as T;
}

export function getUserList(value: unknown): AssignableUser[] {
  const data = getResponseData<
    UserListViewModel | UserViewModel[]
  >(
    value as
      | UserListViewModel
      | UserViewModel[]
      | { data: UserListViewModel | UserViewModel[] }
      | undefined,
  );
  const users = Array.isArray(data) ? data : data?.users ?? [];
  return users.map((user) => ({
    id: user.id,
    displayName: user.displayName,
    email: user.email,
  }));
}

export function getLinkableJobs(
  value: unknown,
  currentJobId: string | undefined,
): LinkableJob[] {
  const data = getResponseData<JobListItemViewModel[]>(
    value as
      | JobListItemViewModel[]
      | { data: JobListItemViewModel[] }
      | undefined,
  );
  const jobs = Array.isArray(data) ? data : [];

  return jobs
    .filter((job) => job.id !== currentJobId)
    .map((job) => ({
      id: job.id,
      label: `SAG-${(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}`,
      description: `${job.customer?.name || 'Ukendt kunde'}\n${job.customer?.address || ''}`,
    }));
}

export function toForm(job: JobReportSummaryViewModel): JobForm {
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

export function toUpdateRequest(
  job: JobReportSummaryViewModel,
  initial: JobForm,
  form: JobForm,
): UpdateJobRequest {
  return {
    customer: sameCustomer(initial.customer, form.customer)
      ? null
      : form.customer,
    reportNumber: job.reportNumber
      ? null
      : (initial.reportNumber !== form.reportNumber ? form.reportNumber.trim() || null : null),
    work: null,
    observations:
      initial.taskDescription !== form.taskDescription ||
      initial.customerObservations !== form.customerObservations
        ? {
            reportDate: job.observations.reportDate ?? null,
            taskDescription: form.taskDescription.trim() || null,
            customerObservations: form.customerObservations.trim() || null,
            technicalObservations: job.observations.technicalObservations ?? null,
          }
        : null,
  };
}

export function sameForm(left: JobForm, right: JobForm) {
  return JSON.stringify(left) === JSON.stringify(right);
}

export function sameCustomer(left: CustomerInfo, right: CustomerInfo) {
  return (
    left.customerId === right.customerId &&
    left.name === right.name &&
    left.address === right.address &&
    left.email === right.email &&
    left.contactPerson === right.contactPerson &&
    left.phone === right.phone
  );
}

export function isValidContactInfo(customer: CustomerInfo) {
  return (
    validateEmail(customer.email) === null &&
    validatePhoneNumber(customer.phone) === null
  );
}

export function toNullable(value: string | null) {
  return value && value.length > 0 ? value : null;
}
