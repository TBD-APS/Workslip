import { describe, expect, it } from 'vitest';
import type { ReferenceDataResponse } from '../../../api/generated/models';
import type { JobForm } from '../types';
import {
  getJobStepValidationIssues,
  getJobValidationIssues,
  mapBackendValidationIssues,
} from './jobValidation';

const referenceData = {
  closureFlags: [],
  installationTypes: [
    {
      id: 'installation-1',
      name: 'Vand',
      sortOrder: 1,
      categories: [
        {
          id: 'category-1',
          name: 'afløb',
          sortOrder: 1,
          controlPoints: [
            { id: 'cp-1', name: 'Kontrol', sortOrder: 1, isRequired: true },
          ],
        },
      ],
    },
  ],
  workKinds: [
    { label: 'Andet', normalizedLabel: 'Andet', sortOrder: 1, requiresCustomWorkKind: true },
  ],
} as unknown as ReferenceDataResponse;

function form(overrides: Partial<JobForm> = {}): JobForm {
  return {
    customerId: null,
    customerSnapshot: {
      name: 'Kunde',
      email: 'kunde@example.dk',
      phone: '12345678',
      address: null,
      contactPerson: null,
    },
    editSnapshot: false,
    createCustomer: false,
    reportNumber: '42',
    destinationAddress: '',
    destinationZipCode: '',
    destinationCity: '',
    taskDescription: '',
    customerObservations: '',
    technicalObservations: '',
    jobType: 'KLS',
    timesheets: [],
    work: {
      categoryIds: ['installation-1'],
      workKind: 'Service',
      customWorkKind: '',
      controlPointSelections: { 'cp-1': true },
      irrelevantCategoryIds: [],
      allIrrelevantReason: '',
      closureFlags: ['Færdig'],
    },
    ...overrides,
  };
}

describe('actionable job validation', () => {
  it('returns a concrete target and action for invalid customer fields', () => {
    const targetForm = form({
      customerSnapshot: {
        name: '',
        email: 'not-an-email',
        phone: '1',
        address: null,
        contactPerson: null,
      },
    });

    const issues = getJobStepValidationIssues({
      form: targetForm,
      referenceData,
      worksheetCount: 1,
      reportNumberReadOnly: true,
    }, 0);

    expect(issues.map((entry) => entry.targetId)).toEqual([
      'customerName',
      'email',
      'phone',
    ]);
    expect(issues[0]).toMatchObject({ step: 0, actionLabel: 'Udfyld kundenavn' });
  });

  it('points control-point validation at the exact invalid category', () => {
    const targetForm = form({
      work: {
        ...form().work,
        controlPointSelections: {},
      },
    });

    expect(getJobStepValidationIssues({
      form: targetForm,
      referenceData,
      worksheetCount: 1,
      reportNumberReadOnly: true,
    }, 2)[0]).toMatchObject({
      step: 2,
      targetId: 'job-control-category-installation-1-category-1',
      actionLabel: 'Ret kontrolpunkt',
    });
  });

  it('collects issues across steps in the order the user meets them', () => {
    const targetForm = form({
      work: {
        ...form().work,
        categoryIds: [],
        closureFlags: [],
      },
    });

    const issues = getJobValidationIssues({
      form: targetForm,
      referenceData,
      worksheetCount: 0,
      reportNumberReadOnly: true,
    });

    expect(issues.map((entry) => entry.step)).toEqual([1, 3, 4]);
    expect(issues.map((entry) => entry.actionLabel)).toEqual([
      'Vælg anlægstype',
      'Tilføj timeseddel',
      'Vælg afslutningsstatus',
    ]);
  });

  it('maps structured backend fields to the corresponding wizard target without message matching', () => {
    const issues = mapBackendValidationIssues([
      { field: 'Customer.Name', message: 'Kundenavn er påkrævet.' },
      {
        field: 'InstallationTypes.installation-1.Categories.category-1.ControlPoints',
        message: 'Mindst et kontrolpunkt skal vælges.',
      },
      { field: 'Worksheets', message: 'Timeseddel mangler.' },
      { field: 'ClosureFlags', message: 'Afslutningsstatus mangler.' },
    ]);

    expect(issues.map((entry) => [entry.step, entry.targetId])).toEqual([
      [0, 'customerName'],
      [2, 'job-control-category-installation-1-category-1'],
      [3, 'job-worksheet-add-trigger'],
      [4, 'job-closure-flags'],
    ]);
  });

  it('keeps unknown backend fields visible on attestation instead of discarding them', () => {
    expect(mapBackendValidationIssues([
      { field: 'Unexpected.Rule', message: 'Noget skal rettes.' },
    ])[0]).toMatchObject({
      step: 5,
      targetId: 'job-attestation-validation',
      message: 'Noget skal rettes.',
    });
  });
});
