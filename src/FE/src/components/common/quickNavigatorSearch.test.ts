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

describe('quick navigator global search', () => {
  it('fans ordinary text out to both jobs and customers', () => {
    expect(getQuickJobSearchTerm('niels')).toBe('niels');
    expect(getCustomerSearchTerm('niels')).toBe('niels');
    expect(getQuickJobSearchTerm('  Viborgvej  ')).toBe('Viborgvej');
    expect(getCustomerSearchTerm('  Viborgvej  ')).toBe('Viborgvej');
    expect(getQuickJobSearchTerm('1234')).toBe('1234');
    expect(getCustomerSearchTerm('1234')).toBe('1234');
  });

  it('supports explicit job intent as an optional precision prefix', () => {
    expect(getQuickJobSearchTerm('sag 1234')).toBe('1234');
    expect(getQuickJobSearchTerm('job #AB12')).toBe('AB12');
    expect(getCustomerSearchTerm('sag 1234')).toBeNull();
    expect(getCustomerSearchTerm('job #AB12')).toBeNull();
  });

  it('supports explicit customer intent as an optional precision prefix', () => {
    expect(getCustomerSearchTerm('kunde Niels')).toBe('Niels');
    expect(getCustomerSearchTerm('kunde #1234')).toBe('1234');
    expect(getQuickJobSearchTerm('kunde Niels')).toBeNull();
  });

  it('requires at least 2 characters for remote search', () => {
    expect(getQuickJobSearchTerm('')).toBeNull();
    expect(getCustomerSearchTerm('')).toBeNull();
    expect(getQuickJobSearchTerm('a')).toBeNull();
    expect(getCustomerSearchTerm('a')).toBeNull();
    expect(getQuickJobSearchTerm('sag 1')).toBeNull();
    expect(getCustomerSearchTerm('kunde a')).toBeNull();
    expect(getQuickJobSearchTerm('ab')).toBe('ab');
    expect(getCustomerSearchTerm('ab')).toBe('ab');
  });

  it('does not mistake words that merely start like an intent for prefixes', () => {
    expect(getQuickJobSearchTerm('saggy pants')).toBe('saggy pants');
    expect(getCustomerSearchTerm('saggy pants')).toBe('saggy pants');
    expect(getQuickJobSearchTerm('jobless')).toBe('jobless');
    expect(getCustomerSearchTerm('jobless')).toBe('jobless');
    expect(getQuickJobSearchTerm('kundeservice')).toBe('kundeservice');
    expect(getCustomerSearchTerm('kundeservice')).toBe('kundeservice');
  });

  it('keeps non-admin quick results scoped to assigned jobs', () => {
    const jobs = [job('mine', ['user-1']), job('other', ['user-2'])];

    expect(filterQuickNavigationJobs(jobs, false, 'user-1').map((item) => item.id))
      .toEqual(['mine']);
    expect(filterQuickNavigationJobs(jobs, false, undefined)).toEqual([]);
    expect(filterQuickNavigationJobs(jobs, true, undefined)).toEqual(jobs);
  });
});
