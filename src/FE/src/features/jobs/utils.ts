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
  reportNumber?: string | null;
  createCustomerFromSnapshot?: boolean | null;
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
  createCustomer: false,
  reportNumber: '',
  destinationAddress: '',
  destinationZipCode: '',
  destinationCity: '',
  taskDescription: '',
  customerObservations: '',
  technicalObservations: '',
  work: {
    categoryIds: [],
    workKind: '',
    customWorkKind: '',
    controlPointSelections: {},
    irrelevantCategoryIds: [],
    allIrrelevantReason: '',
    closureFlags: [],
  },
   jobType: 'KLS',
  timesheets: [],
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
      description: `${job.customer?.name || 'Ukendt kunde'}`,
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

  const timesheets = job.worksheets?.map(ws => ({
    workDate: ws.workDate,
    userId: ws.userId,
    hours: String(ws.hoursWorked),
    sleptOnJob: ws.sleptOnJob,
  })) ?? [];

  return {
    customerId: job.customerId ?? null,
    createCustomer: false,
    customerSnapshot: {
      name: job.customerSnapshot.name ?? null,
      address: job.customerSnapshot.address ?? null,
      email: job.customerSnapshot.email ?? null,
      contactPerson: job.customerSnapshot.contactPerson ?? null,
      phone: job.customerSnapshot.phone ?? null,
    },
    editSnapshot: false,
    reportNumber: job.reportNumber ?? '',
    destinationAddress: job.destinationAddress ?? '',
    destinationZipCode: job.destinationZipCode ?? '',
    destinationCity: job.destinationCity ?? '',
    taskDescription: job.observations.taskDescription ?? '',
    customerObservations: job.observations.customerObservations ?? '',
    technicalObservations: job.observations.technicalObservations ?? '',
    work: {
      categoryIds: job.work.installationTypes.map((installationType) => installationType.id),
      workKind: job.work.workKind?.normalizedLabel ?? '',
      customWorkKind: job.work.workKind?.customWorkKind ?? '',
      controlPointSelections,
      irrelevantCategoryIds,
      allIrrelevantReason: job.work.remarks ?? '',
      closureFlags: job.work.closureFlags ? job.work.closureFlags.map((flag) => flag.normalizedLabel) : [],
    },
    jobType: job.jobType === 'Diverse' ? 'Diverse' : 'KLS',
    timesheets,
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
  const customerCreationAlreadyPersisted =
    form.createCustomer &&
    initial.customerId !== null &&
    initial.customerId !== form.customerId;
  const shouldCreateCustomer = form.createCustomer && !customerCreationAlreadyPersisted;
  const shouldSendCustomerSnapshot =
    shouldCreateCustomer ||
    (hasSnapshotData(form.customerSnapshot) && !sameSnapshot(initial.customerSnapshot, form.customerSnapshot));

  const snapshot = shouldSendCustomerSnapshot
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
    createCustomerFromSnapshot: shouldCreateCustomer || undefined,
    destinationAddress: initial.destinationAddress !== form.destinationAddress ? form.destinationAddress.trim() || null : null,
    destinationZipCode: initial.destinationZipCode !== form.destinationZipCode ? form.destinationZipCode.trim() || null : null,
    destinationCity: initial.destinationCity !== form.destinationCity ? form.destinationCity.trim() || null : null,
    reportNumber: job.reportNumber
      ? null
      : (initial.reportNumber !== form.reportNumber ? form.reportNumber.trim() || null : null),
    // Whether work is sent at all is only about the user: did they touch it,
    // and is this a caller that carries work. WHICH installationTypes value is
    // faithful - a list, or `null` for "leave the recorded ones alone" - is
    // decided in `toWorkRequest`, the only place that builds the list, so no
    // caller can emit a wipe and none has to withhold the whole write to avoid
    // one.
    work: includeWork && !sameWork(initial, form) ? toWorkRequest(form, referenceData) : null,
    observations:
      initial.taskDescription !== form.taskDescription ||
      initial.customerObservations !== form.customerObservations ||
      initial.technicalObservations !== form.technicalObservations
        ? {
            reportDate: null,
            taskDescription: form.taskDescription.trim() || null,
            customerObservations: form.customerObservations.trim() || null,
            technicalObservations: form.technicalObservations.trim() || null,
          }
        : null,
  };
}

/**
 * Serialise `form.work` into the work slice of a PATCH.
 *
 * `installationTypes` is nullable in the contract, and the two values mean very
 * different things to the API: a list REPLACES the recorded installations -
 * taking their kontrolpunkt ticks and isIrrelevant markers with it - while
 * `null` leaves them exactly as they are and still applies opgavetype,
 * afslutningsstatus and bemærkning. `EfJobRepository` only calls
 * `SyncSelectedInstallationsAsync` `if (request.Work.InstallationTypes is not
 * null)`.
 *
 * The list is the selection intersected with the anlægstype catalogue, so
 * `null` is the honest answer whenever that intersection says nothing about
 * what the user wants:
 *
 * - Empty selection -> `[]`. Nothing needs resolving, and clearing the
 *   selection is a deliberate "remove them all" that the API is meant to
 *   honour.
 * - A selection the catalogue resolves nothing of -> `null`. The catalogue is
 *   either not loaded or deliberately empty: `/api/reference-data` strips
 *   `installationTypes` for a tenant without the compliance-evidence module
 *   while `GET /api/jobs/{id}` keeps them, so an empty catalogue is a permanent
 *   server answer and not a loading state. Emitting the empty intersection
 *   would delete every recorded installation; refusing the whole write instead
 *   left the sag neither savable nor leavable, because step 4 demands an
 *   afslutningsstatus that lands in `form.work` and so keeps the write pending
 *   for ever.
 * - A selection the catalogue resolves in part -> the resolved subset. The
 *   missing ids are gone from the catalogue, so the work step can neither
 *   render nor re-select them: the list the user is looking at is exactly the
 *   list that reaches the API.
 *
 * `remarks` is catalogue-derived in the same way - it only means anything when
 * every selected category is irrelevant - so the "resolves nothing" case leaves
 * it as the server has it rather than nulling a bemærkning that cannot be seen
 * or reached while the catalogue is empty.
 */
export function toWorkRequest(
  form: JobForm,
  referenceData: ReferenceDataResponse | null,
): CreateJobWorkRequest {
  const selectedTypes = referenceData?.installationTypes
    .filter((type) => form.work.categoryIds.includes(type.id)) ?? [];
  const resolvesNothing = form.work.categoryIds.length > 0 && selectedTypes.length === 0;

  return {
    installationTypes: resolvesNothing
      ? null
      : selectedTypes.map((type) => ({
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
    // In the resolves-nothing case this echoes the persisted bemaerkning back
    // unchanged (modulo trim): allIrrelevantReason is seeded from
    // job.work.remarks and is only editable in ControlPointsStep, which cannot
    // render without a resolvable selection. Remarks has no null-skip on the
    // server, so omitting it would delete it.
    remarks: resolvesNothing || areAllSelectedCategoriesIrrelevant(form, referenceData)
      ? form.work.allIrrelevantReason.trim() || null
      : null,
  };
}

export function areAllSelectedCategoriesIrrelevant(
  form: JobForm,
  referenceData: ReferenceDataResponse | null,
) {
  const selectedCategories = referenceData?.installationTypes
    .filter((type) => form.work.categoryIds.includes(type.id))
    .flatMap((type) => type.categories.map((category) => `${type.id}-${category.id}`)) ?? [];

  return selectedCategories.length > 0 &&
    selectedCategories.every((id) => form.work.irrelevantCategoryIds.includes(id));
}

export function sameForm(left: JobForm, right: JobForm) {
  return JSON.stringify({ ...left, editSnapshot: false }) ===
    JSON.stringify({ ...right, editSnapshot: false });
}

export function sameFormWithoutWork(left: JobForm, right: JobForm) {
  const customerCreationWasPersisted =
    right.createCustomer &&
    left.customerId !== null &&
    left.customerId !== right.customerId &&
    sameSnapshot(left.customerSnapshot, right.customerSnapshot);

  return (
    (left.customerId === right.customerId || customerCreationWasPersisted) &&
    (left.createCustomer === right.createCustomer || customerCreationWasPersisted) &&
    sameSnapshot(left.customerSnapshot, right.customerSnapshot) &&
    left.reportNumber === right.reportNumber &&
    left.destinationAddress === right.destinationAddress &&
    left.destinationZipCode === right.destinationZipCode &&
    left.destinationCity === right.destinationCity &&
    left.taskDescription === right.taskDescription &&
    left.customerObservations === right.customerObservations &&
    left.technicalObservations === right.technicalObservations
  );
}

export function sameCustomer(left: JobForm, right: JobForm) {
  return left.customerId === right.customerId;
}

function snapshotVal(v: string | null | undefined): string {
  return v ?? '';
}

export function sameSnapshot(
  left: CustomerSnapshotData | null,
  right: CustomerSnapshotData | null,
) {
  if (left === right) return true;
  if (!left || !right) return false;
  return (
    snapshotVal(left.name) === snapshotVal(right.name) &&
    snapshotVal(left.email) === snapshotVal(right.email) &&
    snapshotVal(left.phone) === snapshotVal(right.phone) &&
    snapshotVal(left.address) === snapshotVal(right.address) &&
    snapshotVal(left.contactPerson) === snapshotVal(right.contactPerson)
  );
}

export function sameWork(left: JobForm, right: JobForm) {
  return JSON.stringify(left.work) === JSON.stringify(right.work);
}

export function isValidJobForm(form: JobForm, options?: { reportNumberReadOnly?: boolean; requireDestinationAddress?: boolean }) {
  if (form.jobType === 'Diverse') {
    return options?.reportNumberReadOnly || form.reportNumber.trim().length > 0;
  }

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

export function isValidCreateForm(form: JobForm, options?: { requireDestinationAddress?: boolean }) {
  return isValidJobForm(form, { reportNumberReadOnly: true, ...options });
}

export function isValidWork(form: JobForm, referenceData: ReferenceDataResponse | null) {
  return getWorkValidationMessage(form, referenceData) === null;
}

export function getWorkValidationMessage(form: JobForm, referenceData: ReferenceDataResponse | null) {
  if (form.jobType === 'Diverse') return null;

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
