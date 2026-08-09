import { describe, expect, it } from 'vitest';
import type { ReferenceDataResponse } from '../../api/generated/models';
import type { JobForm } from './types';
import { emptyForm, toWorkRequest } from './utils';

const referenceData = {
  installationTypes: [
    {
      id: 'type-1',
      name: 'Vand',
      sortOrder: 1,
      categories: [
        {
          id: 'category-1',
          name: 'Kontrol',
          sortOrder: 1,
          controlPoints: [
            {
              id: 'cp-1',
              name: 'Kontrolpunkt',
              description: null,
              sortOrder: 1,
              isRequired: false,
            },
          ],
        },
      ],
    },
  ],
  workKinds: [],
  closureFlags: [],
} as unknown as ReferenceDataResponse;

function formWithComment(comment: string): JobForm {
  return {
    ...emptyForm,
    work: {
      ...emptyForm.work,
      categoryIds: ['type-1'],
      controlPointComments: { 'cp-1': comment },
    },
  };
}

describe('control point comments', () => {
  it('includes a trimmed comment with the control point request', () => {
    const request = toWorkRequest(formWithComment('  Forklaring fra montør  '), referenceData);

    expect(request.installationTypes?.[0]?.categories?.[0]?.controlPoints?.[0]?.comment)
      .toBe('Forklaring fra montør');
  });

  it('sends an empty comment as null', () => {
    const request = toWorkRequest(formWithComment('   '), referenceData);

    expect(request.installationTypes?.[0]?.categories?.[0]?.controlPoints?.[0]?.comment)
      .toBeNull();
  });
});
