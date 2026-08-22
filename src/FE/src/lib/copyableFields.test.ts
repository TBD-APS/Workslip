import { describe, expect, it } from 'vitest';
import {
  domainFieldPolicyRegistry,
  getCallHref,
  getCopySuccessMessage,
  getDomainFieldPolicy,
  getEmailHref,
  normalizeDomainValue,
} from './copyableFields';

describe('domainFieldPolicyRegistry', () => {
  it('owns explicit copyability and action decisions', () => {
    expect(Object.keys(domainFieldPolicyRegistry)).toEqual(expect.arrayContaining([
      'customer.name',
      'customer.phone',
      'customer.email',
      'customer.jobCount',
      'address.full',
      'user.name',
      'user.phone',
      'user.email',
      'user.role',
      'job.reportNumber',
      'job.status',
      'job.taskDescription',
    ]));

    expect(getDomainFieldPolicy('customer.phone')).toMatchObject({
      copyable: true,
      actions: ['copy', 'call'],
    });
    expect(getDomainFieldPolicy('customer.email')).toMatchObject({
      copyable: true,
      actions: ['copy', 'email'],
    });
    expect(getDomainFieldPolicy('user.role')).toMatchObject({ copyable: false, actions: [] });
    expect(getDomainFieldPolicy('job.taskDescription')).toMatchObject({ copyable: false, actions: [] });
  });

  it('keeps explicit copyable decisions consistent with the action list', () => {
    for (const policy of Object.values(domainFieldPolicyRegistry)) {
      expect(policy.copyable).toBe(policy.actions.includes('copy'));
    }
  });

  it('normalizes whitespace without mutating meaningful field content', () => {
    expect(normalizeDomainValue('customer.name', '  Niels   Petersen  ')).toBe('Niels Petersen');
    expect(normalizeDomainValue('address.full', '  Testvej 1,   8000 Aarhus C  ')).toBe('Testvej 1, 8000 Aarhus C');
    expect(normalizeDomainValue('customer.phone', '  +45 12 34 56 78  ')).toBe('+45 12 34 56 78');
    expect(normalizeDomainValue('customer.email', '  Kunde@Example.dk  ')).toBe('Kunde@Example.dk');
  });

  it('builds platform-native call and e-mail actions centrally', () => {
    expect(getCallHref('+45 12 34 56 78')).toBe('tel:+4512345678');
    expect(getEmailHref('  Kunde@Example.dk  ')).toBe('mailto:Kunde@Example.dk');
  });

  it('returns empty for missing values and central success copy', () => {
    expect(normalizeDomainValue('user.email', null)).toBe('');
    expect(getCopySuccessMessage('job.reportNumber')).toBe('Sagsnummer kopieret');
  });
});
