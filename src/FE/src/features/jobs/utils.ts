import type {
  CreateJobWorkRequest,
  CustomerInfo,
  JobReportSummaryViewModel,
  UpdateJobRequest,
} from '../../api/generated/models';
import { validateEmail, validatePhoneNumber } from '../../components/forms/validators';
import type { AssignableUser, JobForm, LinkableJob, ReferenceData } from './types';

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
  work: {
    categoryIds: [],
    workKind: '',
    customWorkKind: '',
  },
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
    work: {
      categoryIds: job.work.installationTypes.map((installationType) => installationType.id),
      workKind: job.work.workKind?.normalizedLabel ?? '',
      customWorkKind: job.work.workKind?.customWorkKind ?? '',
    },
  };
}

export function toUpdateRequest(
  job: JobReportSummaryViewModel,
  initial: JobForm,
  form: JobForm,
  referenceData: ReferenceData | null,
  options: { includeWork?: boolean } = {},
): UpdateJobRequest {
  const includeWork = options.includeWork ?? true;

  return {
    customer: sameCustomer(initial.customer, form.customer)
      ? null
      : form.customer,
    reportNumber: job.reportNumber
      ? null
      : (initial.reportNumber !== form.reportNumber ? form.reportNumber.trim() || null : null),
    work: includeWork && !sameWork(initial, form) ? toWorkRequest(form, referenceData) : null,
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

export function toWorkRequest(
  form: JobForm,
  referenceData: ReferenceData | null,
): CreateJobWorkRequest {
  const selectedCategories = referenceData?.installationTypes
    .filter((category) => form.work.categoryIds.includes(category.id)) ?? [];

  return {
    installationTypes: selectedCategories.map((category) => ({
      id: category.id,
      categories: category.categories.map((subcategory) => ({
        id: subcategory.id,
        controlPoints: subcategory.controlPoints.map((controlPoint) => ({
          id: controlPoint.id,
          sortOrder: controlPoint.sortOrder,
          isRequired: controlPoint.isRequired,
        })),
        isIrrelevant: false,
      })),
    })),
    workKind: form.work.workKind || null,
    customWorkKind: form.work.customWorkKind.trim() || null,
    closureFlags: null,
    remarks: null,
  };
}

export function sameForm(left: JobForm, right: JobForm) {
  return JSON.stringify(left) === JSON.stringify(right);
}

export function sameFormWithoutWork(left: JobForm, right: JobForm) {
  return (
    sameCustomer(left.customer, right.customer) &&
    left.reportNumber === right.reportNumber &&
    left.taskDescription === right.taskDescription &&
    left.customerObservations === right.customerObservations
  );
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

export function sameWork(left: JobForm, right: JobForm) {
  return JSON.stringify(left.work) === JSON.stringify(right.work);
}

export function isValidJobForm(form: JobForm, options?: { reportNumberReadOnly?: boolean }) {
  return (
    (options?.reportNumberReadOnly || form.reportNumber.trim().length > 0) &&
    (form.customer.name?.trim().length ?? 0) > 0 &&
    validateEmail(form.customer.email) === null &&
    validatePhoneNumber(form.customer.phone) === null
  );
}

export function isValidCreateForm(form: JobForm) {
  return isValidJobForm(form);
}

export function isValidWork(form: JobForm, referenceData: ReferenceData | null) {
  return getWorkValidationMessage(form, referenceData) === null;
}

export function getWorkValidationMessage(form: JobForm, referenceData: ReferenceData | null) {
  const selectedWorkKind = referenceData?.workKinds.find(
    (kind) => kind.normalizedLabel === form.work.workKind,
  );

  if (form.work.categoryIds.length === 0) return 'Vælg mindst én kategori.';
  if (form.work.workKind.length === 0) return 'Vælg en arbejdstype.';
  if (selectedWorkKind?.requiresCustomWorkKind && form.work.customWorkKind.trim().length === 0) {
    return 'Udfyld service andet-feltet.';
  }

  return null;
}

export function toNullable(value: string | null) {
  return value && value.length > 0 ? value : null;
}
