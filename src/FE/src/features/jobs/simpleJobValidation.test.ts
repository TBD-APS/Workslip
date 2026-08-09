import { describe, expect, it } from 'vitest';
import { validateControlPoints } from './components/steps/controlPointsValidation';
import type { JobForm } from './types';
import { emptyForm, isValidWork } from './utils';

function simpleJobForm(): JobForm {
  return {
    ...emptyForm,
    jobType: 'Diverse',
    reportNumber: '408',
    work: {
      ...emptyForm.work,
      categoryIds: ['stale-kls-category'],
    },
  };
}

describe('simple job validation', () => {
  it('does not require KLS work metadata when saving a Diverse job', () => {
    expect(isValidWork(simpleJobForm(), null)).toBe(true);
  });

  it('does not validate KLS control points for a Diverse job', () => {
    expect(validateControlPoints(simpleJobForm(), null)).toEqual({ valid: true });
  });
});
