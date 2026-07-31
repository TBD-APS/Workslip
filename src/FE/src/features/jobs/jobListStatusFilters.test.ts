import { beforeEach, describe, expect, it } from 'vitest';
import { JobStatus } from '../../api/generated/models';
import {
  getSavedJobListStatuses,
  JOB_LIST_FILTER_KEY,
} from './jobListStatusFilters';

describe('job list status filter persistence', () => {
  beforeEach(() => {
    sessionStorage.clear();
    sessionStorage.setItem('statusFilter:lastActive', JOB_LIST_FILTER_KEY);
  });

  it('defaults to active and rejected jobs together', () => {
    expect(getSavedJobListStatuses()).toEqual([
      JobStatus.Draft,
      JobStatus.Rejected,
    ]);
  });

  it('migrates a saved active-only selection to the combined group', () => {
    sessionStorage.setItem(
      `statusFilter:${JOB_LIST_FILTER_KEY}`,
      JSON.stringify([JobStatus.Draft]),
    );

    expect(getSavedJobListStatuses()).toEqual([
      JobStatus.Draft,
      JobStatus.Rejected,
    ]);
    expect(JSON.parse(sessionStorage.getItem(`statusFilter:${JOB_LIST_FILTER_KEY}`) ?? '[]')).toEqual([
      JobStatus.Draft,
      JobStatus.Rejected,
    ]);
  });

  it('preserves completed or review-only selections', () => {
    sessionStorage.setItem(
      `statusFilter:${JOB_LIST_FILTER_KEY}`,
      JSON.stringify([JobStatus.InReview]),
    );

    expect(getSavedJobListStatuses()).toEqual([JobStatus.InReview]);
  });
});