import type {
  CreateJobWorkRequest,
  CustomerInfo,
  JobReportSummaryViewModel,
  ReferenceDataResponse,
  UpdateJobRequest,
} from '../../api/generated/models';
import { validateEmail, validatePhoneNumber } from '../../components/forms/validators';
import type { JobForm, LinkableJob } from './types';
import type { CustomerSnapshotData } from './customerSnapshotData';

type UpdateJobRequestWithSnapshot = UpdateJobRequest & {
  customerSnapshot?: CustomerSnapshotData | null;
};

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

export const emptySnapshot: CustomerSnapshotData = {
  name: null,
  email: null,
  phone: null,
  address: null,
};

export const emptyForm: JobForm = {
  customer: { ...emptyCustomer },
  customerSnapshot: null,
  editSnapshot: false,
  reportNumber: '',
  taskDescription: '',
  customerObservations: '',
  technicalObservations: '',
  work: {
    categoryIds: [],
    workKind: '',
    customWorkKind: '',
    controlPointSelections: {},
    irrelevantCategoryIds: [],
    closureFlags: [],
  },
};

export function getLinkableJobs(
  value: JobListItemViewModel[] | undefined,
  currentJobId: string | undefined,
): LinkableJob[] {

  if(value === null || value === undefined)
    return [];

  const jobs = value;

  return jobs
    .filter((job) => job.id !== currentJobId)
    .map((job) => ({
      id: job.id,
      label: `SAG-${(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}`,
      description: `${job.customer?.name || 'Ukendt kunde'}\n${job.customer?.address || ''}`,
    }));
}

export function toForm(job: JobReportSummaryViewModel): JobForm {
  const controlPointSelections: Record<string, boolean> = {};
  const irrelevantCategoryIds: string[] = [];

  for (const instType of job.work.installationTypes) {
    for (const cat of instType.categories) {
      if (cat.isIrrelevant) {
        irrelevantCategoryIds.push(`${instType.id}-${cat.id}`);
      }
      for (const cp of cat.controlPoints) {
        controlPointSelections[cp.id] = cp.isChecked;
      }
    }
  }

  return {
    customer: {
      customerId: job.customer.customerId ?? null,
      name: job.customer.name ?? null,
      address: job.customer.address ?? null,
      email: job.customer.email ?? null,
      contactPerson: job.customer.contactPerson ?? null,
      phone: job.customer.phone ?? null,
    },
    customerSnapshot: null,
    editSnapshot: false,
    reportNumber: job.reportNumber ?? '',
    taskDescription: job.observations.taskDescription ?? '',
    customerObservations: job.observations.customerObservations ?? '',
    technicalObservations: job.observations.technicalObservations ?? '',
    work: {
      categoryIds: job.work.installationTypes.map((installationType) => installationType.id),
      workKind: job.work.workKind?.normalizedLabel ?? '',
      customWorkKind: job.work.workKind?.customWorkKind ?? '',
      controlPointSelections,
      irrelevantCategoryIds,
      closureFlags: job.work.closureFlags ? job.work.closureFlags.map((flag) => flag.normalizedLabel) : [],
    },
  };
}

export function toUpdateRequest(
  job: JobReportSummaryViewModel,
  initial: JobForm,
  form: JobForm,
  referenceData: ReferenceDataResponse | null,
  options: { includeWork?: boolean } = {},
): UpdateJobRequestWithSnapshot {
  const includeWork = options.includeWork ?? true;

  return {
    customer: sameCustomer(initial.customer, form.customer)
      ? null
      : form.customer,
    customerSnapshot: form.editSnapshot ? form.customerSnapshot : null,
    reportNumber: job.reportNumber
      ? null
      : (initial.reportNumber !== form.reportNumber ? form.reportNumber.trim() || null : null),
    work: includeWork && !sameWork(initial, form) ? toWorkRequest(form, referenceData) : null,
    observations:
      initial.taskDescription !== form.taskDescription ||
      initial.customerObservations !== form.customerObservations ||
      initial.technicalObservations !== form.technicalObservations
        ? {
            reportDate: job.observations.reportDate ?? null,
            taskDescription: form.taskDescription.trim() || null,
            customerObservations: form.customerObservations.trim() || null,
            technicalObservations: form.technicalObservations.trim() || null,
          }
        : null,
  };
}

export function toWorkRequest(
  form: JobForm,
  referenceData: ReferenceDataResponse | null,
): CreateJobWorkRequest {
  const selectedTypes = referenceData?.installationTypes
    .filter((type) => form.work.categoryIds.includes(type.id)) ?? [];

  return {
    installationTypes: selectedTypes.map((type) => ({
      id: type.id,
      categories: type.categories.map((cat) => ({
        id: cat.id,
        controlPoints: cat.controlPoints.map((cp) => ({
          id: cp.id,
          sortOrder: cp.sortOrder,
          isRequired: cp.isRequired,
          isChecked: form.work.controlPointSelections[cp.id] ?? false,
        })),
        isIrrelevant: form.work.irrelevantCategoryIds.includes(`${type.id}-${cat.id}`),
      })),
    })),
    workKind: form.work.workKind || null,
    customWorkKind: form.work.customWorkKind.trim() || null,
    closureFlags: form.work.closureFlags || [],
    remarks: null,
  };
}

export function sameForm(left: JobForm, right: JobForm) {
  return JSON.stringify(left) === JSON.stringify(right);
}

export function sameFormWithoutWork(left: JobForm, right: JobForm) {
  return (
    sameCustomer(left.customer, right.customer) &&
    sameSnapshot(left.customerSnapshot, right.customerSnapshot) &&
    left.reportNumber === right.reportNumber &&
    left.taskDescription === right.taskDescription &&
    left.customerObservations === right.customerObservations &&
    left.technicalObservations === right.technicalObservations
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

export function sameSnapshot(
  left: CustomerSnapshotData | null,
  right: CustomerSnapshotData | null,
) {
  if (left === right) return true;
  if (!left || !right) return false;
  return (
    left.name === right.name &&
    left.email === right.email &&
    left.phone === right.phone &&
    left.address === right.address
  );
}

export function sameWork(left: JobForm, right: JobForm) {
  return JSON.stringify(left.work) === JSON.stringify(right.work);
}

export function isValidJobForm(form: JobForm, options?: { reportNumberReadOnly?: boolean }) {
  const name = form.customerSnapshot?.name ?? form.customer.name;
  const email = form.customerSnapshot?.email ?? form.customer.email;
  const phone = form.customerSnapshot?.phone ?? form.customer.phone;

  return (
    (options?.reportNumberReadOnly || form.reportNumber.trim().length > 0) &&
    (name?.trim().length ?? 0) > 0 &&
    validateEmail(email) === null &&
    validatePhoneNumber(phone) === null
  );
}

export function isValidCreateForm(form: JobForm) {
  return isValidJobForm(form);
}

export function isValidWork(form: JobForm, referenceData: ReferenceDataResponse | null) {
  return getWorkValidationMessage(form, referenceData) === null;
}

export function getWorkValidationMessage(form: JobForm, referenceData: ReferenceDataResponse | null) {
  const selectedWorkKind = referenceData?.workKinds.find(
    (kind) => kind.normalizedLabel === form.work.workKind,
  );

  if (form.work.categoryIds.length === 0) return 'Vælg mindst én anlægstype.';
  if (form.work.workKind.length === 0) return 'Vælg en opgavetype.';
  if (selectedWorkKind?.requiresCustomWorkKind && form.work.customWorkKind.trim().length === 0) {
    return 'Udfyld anden opgavetype-feltet.';
  }

  return null;
}

export function toNullable(value: string | null) {
  return value && value.length > 0 ? value : null;
}
