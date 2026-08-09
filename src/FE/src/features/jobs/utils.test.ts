import { describe, expect, it } from 'vitest';
import { emptyForm, isValidCreateForm, isValidJobForm } from './utils';

const validKlsForm = {
  ...emptyForm,
  customerSnapshot: {
    ...emptyForm.customerSnapshot,
    name: 'Testkunde',
  },
};

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
