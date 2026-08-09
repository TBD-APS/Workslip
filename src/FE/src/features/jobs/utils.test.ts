import { describe, expect, it } from 'vitest';
import type { JobForm } from './types';
import { emptyForm, isValidCreateForm, isValidJobForm } from './utils';

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
