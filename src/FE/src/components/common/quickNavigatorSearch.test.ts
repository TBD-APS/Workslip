import { describe, expect, it } from 'vitest';
import type { JobListItemViewModel } from '../../api/generated/models';
import {
  filterQuickNavigationJobs,
  getQuickJobSearchTerm,
  getCustomerSearchTerm,
} from './quickNavigatorSearch';

const job = (id: string, assignedUserIds: string[]) => ({
  id,
  assignedUsers: assignedUserIds.map((userId) => ({ id: userId })),
}) as JobListItemViewModel;

describe('quick navigator job search', () => {
  it('only searches the jobs endpoint for explicit job intent', () => {
    expect(getQuickJobSearchTerm('timer')).toBeNull();
    expect(getQuickJobSearchTerm('kunde')).toBeNull();
    expect(getQuickJobSearchTerm('sag 1234')).toBe('1234');
    expect(getQuickJobSearchTerm('job #AB12')).toBe('AB12');
    expect(getQuickJobSearchTerm('1234')).toBe('1234');
  });

  it('requires at least 2 characters after stripping intent prefix', () => {
    expect(getQuickJobSearchTerm('sag 1')).toBeNull();
    expect(getQuickJobSearchTerm('job A')).toBeNull();
    expect(getQuickJobSearchTerm('sag 12')).toBe('12');
    expect(getQuickJobSearchTerm('1')).toBeNull();
    expect(getQuickJobSearchTerm('12')).toBe('12');
  });

  it('keeps non-admin quick results scoped to assigned jobs', () => {
    const jobs = [job('mine', ['user-1']), job('other', ['user-2'])];

    expect(filterQuickNavigationJobs(jobs, false, 'user-1').map((item) => item.id))
      .toEqual(['mine']);
    expect(filterQuickNavigationJobs(jobs, false, undefined)).toEqual([]);
    expect(filterQuickNavigationJobs(jobs, true, undefined)).toEqual(jobs);
  });
});

describe('quick navigator customer search', () => {
  it('returns null for empty or short queries', () => {
    expect(getCustomerSearchTerm('')).toBeNull();
    expect(getCustomerSearchTerm('  ')).toBeNull();
    expect(getCustomerSearchTerm('a')).toBeNull();
  });

  it('returns the trimmed query for normal text >= 2 chars', () => {
    expect(getCustomerSearchTerm('acme')).toBe('acme');
    expect(getCustomerSearchTerm('  acme  ')).toBe('acme');
    expect(getCustomerSearchTerm('ab')).toBe('ab');
  });

  it('excludes explicit job intent from customer search', () => {
    expect(getCustomerSearchTerm('sag 1234')).toBeNull();
    expect(getCustomerSearchTerm('job #AB12')).toBeNull();
    expect(getCustomerSearchTerm('1234')).toBeNull();
    expect(getCustomerSearchTerm('sag')).toBeNull();
    expect(getCustomerSearchTerm('job')).toBeNull();
  });

  it('allows mixed text that does not start with job intent', () => {
    expect(getCustomerSearchTerm('acme 1234')).toBe('acme 1234');
    expect(getCustomerSearchTerm('saggy pants')).toBe('saggy pants');
    expect(getCustomerSearchTerm('jobless')).toBe('jobless');
  });
});
