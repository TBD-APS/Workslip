import { describe, expect, it } from 'vitest';
import type { JobListItemViewModel } from '../../api/generated/models';
import { filterQuickNavigationJobs, getQuickJobSearchTerm } from './quickNavigatorSearch';

const job = (id: string, assignedUserIds: string[]) => ({
  id,
  assignedUsers: assignedUserIds.map((userId) => ({ id: userId })),
}) as JobListItemViewModel;

describe('quick navigator job search', () => {
  it('searches jobs from ordinary craft-worker input without requiring a sag prefix', () => {
    expect(getQuickJobSearchTerm('a')).toBeNull();
    expect(getQuickJobSearchTerm('1234')).toBe('1234');
    expect(getQuickJobSearchTerm('sag 1234')).toBe('1234');
    expect(getQuickJobSearchTerm('job #AB12')).toBe('AB12');
    expect(getQuickJobSearchTerm('Niels VVS')).toBe('Niels VVS');
    expect(getQuickJobSearchTerm('Vestergade 12')).toBe('Vestergade 12');
  });

  it('keeps non-admin quick results scoped to assigned jobs', () => {
    const jobs = [job('mine', ['user-1']), job('other', ['user-2'])];

    expect(filterQuickNavigationJobs(jobs, false, 'user-1').map((item) => item.id))
      .toEqual(['mine']);
    expect(filterQuickNavigationJobs(jobs, false, undefined)).toEqual([]);
    expect(filterQuickNavigationJobs(jobs, true, undefined)).toEqual(jobs);
  });
});
