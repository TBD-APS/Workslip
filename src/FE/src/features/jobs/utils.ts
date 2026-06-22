import type {
  CreateJobWorkRequest,
  JobReportSummaryViewModel,
  ReferenceDataResponse,
  UpdateJobRequest,
  JobListItemViewModel as GeneratedJobListItemViewModel,
 
} from '../../api/generated/models';
import { validateEmail, validatePhoneNumber } from '../../components/forms/validators';
import type { JobForm, LinkableJob } from './types';
import type { CustomerSnapshotData } from '../../api/generated/models/customerSnapshotData';
import { hasSnapshotData } from './hooks/useCustomerSnapshot';

type UpdateJobRequestWithSnapshot = UpdateJobRequest & {
  customerSnapshot?: CustomerSnapshotData | null;
};

export const emptySnapshot: CustomerSnapshotData = {
  name: null,
  email: null,
  phone: null,
  address: null,
  contactPerson: null
};

export const emptyForm: JobForm = {
  customerId: null,
  customerSnapshot: { ...emptySnapshot },
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
  value: GeneratedJobListItemViewModel[] | undefined,
  currentJobId: string | undefined,
): LinkableJob[] {

  if (value === null || value === undefined)
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
    customerId: job.customerId ?? null,
    customerSnapshot: {
      name: job.customerSnapshot.name ?? null,
      address: job.customerSnapshot.address ?? null,
      email: job.customerSnapshot.email ?? null,
      contactPerson: job.customerSnapshot.contactPerson ?? null,
      phone: job.customerSnapshot.phone ?? null,
    },
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

  // Send `customerSnapshot` whenever it carries data — gated by
  // data presence, NOT by `editSnapshot`. Picking an existing customer
  // populates the snapshot from the pick; toggling the edit checkbox
  // just lets the user mutate those values. Either way the snapshot
  // is the wire shape the repository dereferences to write customer
  // fields onto the job row, so dropping it caused NREs.
  const snapshot = hasSnapshotData(form.customerSnapshot)
    ? {
        name: form.customerSnapshot?.name?.trim() || null,
        address: form.customerSnapshot?.address?.trim() || null,
        email: form.customerSnapshot?.email?.trim() || null,
        contactPerson: form.customerSnapshot?.contactPerson?.trim() || null,
        phone: form.customerSnapshot?.phone?.trim() || null,
      }
    : null;

  return {
    customerSnapshot: snapshot,
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
    left.customerId === right.customerId &&
    sameSnapshot(left.customerSnapshot, right.customerSnapshot) &&
    left.reportNumber === right.reportNumber &&
    left.taskDescription === right.taskDescription &&
    left.customerObservations === right.customerObservations &&
    left.technicalObservations === right.technicalObservations
  );
}

export function sameCustomer(left: JobForm, right: JobForm) {
  return left.customerId === right.customerId;
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
    left.address === right.address &&
    left.contactPerson === right.contactPerson
  );
}

export function sameWork(left: JobForm, right: JobForm) {
  return JSON.stringify(left.work) === JSON.stringify(right.work);
}

export function isValidJobForm(form: JobForm, options?: { reportNumberReadOnly?: boolean }) {
  const name = form.customerSnapshot?.name ?? null;
  const email = form.customerSnapshot?.email ?? null;
  const phone = form.customerSnapshot?.phone ?? null;

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
