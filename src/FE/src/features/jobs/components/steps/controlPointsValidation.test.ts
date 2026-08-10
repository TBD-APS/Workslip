import { describe, expect, it } from 'vitest';
import type { ReferenceDataResponse } from '../../../../api/generated/models';
import { emptyForm } from '../../utils';
import { validateControlPoints } from './controlPointsValidation';

const typeId = '00000000-0000-0000-0000-000000000001';
const categoryId = '00000000-0000-0000-0000-000000000002';
const controlPointId = '00000000-0000-0000-0000-000000000003';
const secondCategoryId = '00000000-0000-0000-0000-000000000004';
const secondControlPointId = '00000000-0000-0000-0000-000000000005';

const referenceData = {
  installationTypes: [{
    id: typeId,
    name: 'Vand',
    sortOrder: 1,
    categories: [
      {
        id: categoryId,
        name: 'installation',
        sortOrder: 1,
        controlPoints: [{ id: controlPointId, name: 'Trykprøve', sortOrder: 1, isRequired: true }],
      },
      {
        id: secondCategoryId,
        name: 'afløb',
        sortOrder: 2,
        controlPoints: [{ id: secondControlPointId, name: 'Tæthed', sortOrder: 1, isRequired: true }],
      },
    ],
  }],
} as ReferenceDataResponse;

describe('validateControlPoints', () => {
  it('accepts a job where every selected category is irrelevant', () => {
    const form = {
      ...emptyForm,
      work: {
        ...emptyForm.work,
        categoryIds: [typeId],
        irrelevantCategoryIds: [`${typeId}-${categoryId}`, `${typeId}-${secondCategoryId}`],
      },
    };

    expect(validateControlPoints(form, referenceData)).toEqual({ valid: true });
  });

  it('still requires a selection in relevant categories', () => {
    const form = {
      ...emptyForm,
      work: { ...emptyForm.work, categoryIds: [typeId] },
    };

    expect(validateControlPoints(form, referenceData).valid).toBe(false);
  });

  it('only evaluates relevant categories when statuses are mixed', () => {
    const form = {
      ...emptyForm,
      work: {
        ...emptyForm.work,
        categoryIds: [typeId],
        irrelevantCategoryIds: [`${typeId}-${categoryId}`],
        controlPointSelections: { [secondControlPointId]: true },
      },
    };

    expect(validateControlPoints(form, referenceData)).toEqual({ valid: true });
  });
});
