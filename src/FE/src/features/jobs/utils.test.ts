import { describe, expect, it } from 'vitest';
import type { JobForm } from './types';
import type { JobReportSummaryViewModel, ReferenceDataResponse } from '../../api/generated/models';
import { JobStatus } from '../../api/generated/models';
import { emptyForm, isValidCreateForm, isValidJobForm, toUpdateRequest, toWorkRequest } from './utils';

const validKlsForm: JobForm = {
  ...emptyForm,
  customerSnapshot: {
    name: 'Testkunde',
    email: null,
    phone: null,
    address: null,
    contactPerson: null,
  },
};

const persistedJob: JobReportSummaryViewModel = {
  assignedUsers: [],
  customerId: null,
  customerSnapshot: {
    address: null,
    contactPerson: null,
    email: null,
    name: null,
    phone: null,
  },
  destinationAddress: null,
  destinationCity: null,
  destinationZipCode: null,
  id: 'job-1',
  jobType: 'KLS',
  links: [],
  observations: {
    customerObservations: null,
    taskDescription: null,
    technicalObservations: null,
  },
  organizationId: 'organization-1',
  rejectionNote: null,
  reportNumber: '1234',
  softDeleted: false,
  status: JobStatus.Draft,
  totalHours: 0,
  totalOutlay: 0,
  work: {
    closureFlags: [],
    installationTypes: [],
    remarks: null,
    workKind: null,
  },
  worksheets: [],
};

const referenceData = {
  closureFlags: [],
  installationTypes: [],
  workKinds: [],
} as ReferenceDataResponse;

const resolvedReferenceData = {
  closureFlags: [],
  installationTypes: [{
    id: 'type-1',
    name: 'Vand',
    sortOrder: 1,
    categories: [{
      id: 'category-1',
      name: 'installation',
      sortOrder: 1,
      controlPoints: [{ id: 'control-point-1', name: 'Tæthed', sortOrder: 1, isRequired: true }],
    }],
  }],
  workKinds: [],
} as unknown as ReferenceDataResponse;

function formWithCategories(categoryIds: string[]): JobForm {
  return {
    ...emptyForm,
    work: { ...emptyForm.work, categoryIds, workKind: 'Service' },
  };
}

describe('work slice serialisation', () => {
  it('sends the cleared work slice when the user deselects every installation type', () => {
    const initial = formWithCategories(['type-1']);
    const form = formWithCategories([]);

    const request = toUpdateRequest(persistedJob, initial, form, resolvedReferenceData, { includeWork: true });

    // A deliberate clear has to reach the API. Withholding it PATCHes everything
    // except work, and the caller then reports a save that never happened.
    expect(request.work).not.toBeNull();
    expect(request.work?.installationTypes).toEqual([]);
  });

  it('sends the work slice when reference data resolves every selected type', () => {
    const initial = formWithCategories([]);
    const form = formWithCategories(['type-1']);

    const request = toUpdateRequest(persistedJob, initial, form, resolvedReferenceData, { includeWork: true });

    expect(request.work?.installationTypes?.map((type) => type.id)).toEqual(['type-1']);
  });

  it('leaves the recorded installations alone while reference data is unresolved', () => {
    const initial = formWithCategories([]);
    const form = formWithCategories(['type-1']);

    const request = toUpdateRequest(persistedJob, initial, form, null, { includeWork: true });

    // A list built from no catalogue would read as "delete every installation
    // type the job has", so the list is omitted - not the whole write. The rest
    // of the work slice is faithful and still applies.
    expect(request.work).not.toBeNull();
    expect(request.work?.installationTypes).toBeNull();
    expect(request.work?.workKind).toBe('Service');
  });

  it('leaves the recorded installations alone when the catalogue is deliberately empty', () => {
    // `/api/reference-data` returns an empty `installationTypes` for every
    // tenant without the compliance-evidence module, while the job GET keeps
    // them - so this catalogue never fills up, and withholding the write left
    // the sag unsavable and unleavable for ever.
    const initial = formWithCategories(['type-1']);
    const form: JobForm = {
      ...initial,
      work: { ...initial.work, closureFlags: ['Completed'] },
    };

    const request = toUpdateRequest(persistedJob, initial, form, referenceData, { includeWork: true });

    expect(request.work).not.toBeNull();
    expect(request.work?.installationTypes).toBeNull();
    // The one edit step 4 demands reaches the API, which is what breaks the
    // deadlock: nothing keeps `hasUnsavedChanges` true for ever.
    expect(request.work?.closureFlags).toEqual(['Completed']);
  });

  it('drops a selected type the catalogue no longer contains instead of withholding for ever', () => {
    // `toForm` copies `job.work.installationTypes` verbatim, so a sag keeps
    // referencing an anlægstype an admin has since removed from reference data.
    const initial = formWithCategories(['type-1', 'type-removed']);
    const form: JobForm = {
      ...initial,
      work: { ...initial.work, controlPointSelections: { 'control-point-1': true } },
    };

    const request = toUpdateRequest(persistedJob, initial, form, resolvedReferenceData, { includeWork: true });

    // The removed type cannot be rendered or re-selected in the work step, so
    // the list the user is looking at is exactly the one that reaches the API.
    // Refusing the write instead left the sag unsavable and unleavable.
    expect(request.work?.installationTypes?.map((type) => type.id)).toEqual(['type-1']);
  });

  it('still sends the work slice for a Diverse job without installation types', () => {
    const form: JobForm = {
      ...emptyForm,
      jobType: 'Diverse',
      work: {
        ...emptyForm.work,
        categoryIds: [],
        allIrrelevantReason: 'Ikke relevant for opgaven',
      },
    };

    const request = toUpdateRequest(persistedJob, emptyForm, form, referenceData, { includeWork: true });

    expect(request.work).not.toBeNull();
  });
});

describe('toWorkRequest installation types', () => {
  it('sends an empty list for a deliberately cleared selection', () => {
    // `[]` replaces the recorded set, which is exactly what removing every
    // anlægstype on purpose has to do.
    expect(toWorkRequest(formWithCategories([]), resolvedReferenceData).installationTypes).toEqual([]);
    expect(toWorkRequest(formWithCategories([]), null).installationTypes).toEqual([]);
  });

  it('sends the resolved list when the catalogue holds the selection', () => {
    const request = toWorkRequest(formWithCategories(['type-1']), resolvedReferenceData);

    expect(request.installationTypes?.map((type) => type.id)).toEqual(['type-1']);
  });

  it('sends null when the catalogue resolves none of a non-empty selection', () => {
    // The API skips the installation sync entirely for a null list, so the
    // recorded anlægstyper, kontrolpunkter and irrelevans-markeringer survive.
    expect(toWorkRequest(formWithCategories(['type-1']), referenceData).installationTypes).toBeNull();
    expect(toWorkRequest(formWithCategories(['type-1']), null).installationTypes).toBeNull();
  });

  it('keeps the persisted bemærkning when the selection resolves to nothing', () => {
    // `allIrrelevantReason` is seeded from `job.work.remarks` and can only be
    // edited from the kontrolpunkt step, which needs a resolvable selection to
    // render - so echoing it is a no-op write, while null would delete a
    // bemærkning the user cannot even see here.
    const form: JobForm = {
      ...formWithCategories(['type-1']),
      work: { ...formWithCategories(['type-1']).work, allIrrelevantReason: 'Ingen kontrolpunkter relevante' },
    };

    expect(toWorkRequest(form, referenceData).remarks).toBe('Ingen kontrolpunkter relevante');
  });

  it('sends the resolved subset when the catalogue no longer holds every id', () => {
    // A partially resolvable selection is the list the work step renders, so it
    // is also the list the user is looking at when they save.
    const request = toWorkRequest(formWithCategories(['type-1', 'type-removed']), resolvedReferenceData);

    expect(request.installationTypes?.map((type) => type.id)).toEqual(['type-1']);
  });
});

describe('destination address validation', () => {
  it('keeps destination address optional when creating a job', () => {
    expect(isValidCreateForm(validKlsForm)).toBe(true);
  });

  it('keeps destination address optional for legacy edit callers', () => {
    expect(isValidJobForm(
      { ...validKlsForm, reportNumber: '1234' },
      { requireDestinationAddress: true },
    )).toBe(true);
  });
});
