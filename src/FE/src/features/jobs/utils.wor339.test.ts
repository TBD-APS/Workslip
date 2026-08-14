import { describe, expect, it } from 'vitest';
import type { ReferenceDataResponse } from '../../api/generated/models';
import { emptyForm, toWorkRequest } from './utils';

describe('WOR-339 work request', () => {
  it('persists the shared all-irrelevant reason as the job remarks', () => {
    const typeId = '00000000-0000-0000-0000-000000000001';
    const categoryId = '00000000-0000-0000-0000-000000000002';
    const form = {
      ...emptyForm,
      work: {
        ...emptyForm.work,
        categoryIds: [typeId],
        irrelevantCategoryIds: [`${typeId}-${categoryId}`],
        allIrrelevantReason: '  Ikke relevant for opgaven  ',
      },
    };

    const referenceData = {
      installationTypes: [{
        id: typeId,
        name: 'Vand',
        sortOrder: 1,
        categories: [{ id: categoryId, name: 'installation', sortOrder: 1, controlPoints: [] }],
      }],
      workKinds: [],
      closureFlags: [],
    } as ReferenceDataResponse;

    expect(toWorkRequest(form, referenceData).remarks)
      .toBe('Ikke relevant for opgaven');
  });

  it('does not persist a stale reason when a category is relevant', () => {
    const form = {
      ...emptyForm,
      work: {
        ...emptyForm.work,
        categoryIds: ['type-1'],
        allIrrelevantReason: 'Gammel begrundelse',
      },
    };
    const referenceData = {
      installationTypes: [{
        id: 'type-1',
        name: 'Vand',
        sortOrder: 1,
        categories: [{ id: 'category-1', name: 'installation', sortOrder: 1, controlPoints: [] }],
      }],
      workKinds: [],
      closureFlags: [],
    } as ReferenceDataResponse;

    expect(toWorkRequest(form, referenceData).remarks).toBeNull();
  });
});
