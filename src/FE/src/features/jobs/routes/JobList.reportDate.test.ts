import { describe, expect, it } from 'vitest';
import { getJobListReportDate } from './JobList';

describe('getJobListReportDate', () => {
  it('uses the stored report date when one exists', () => {
    expect(getJobListReportDate({
      jobType: 'Diverse',
      reportDate: '2026-07-20',
      createdAt: '2026-07-31T10:00:00Z',
    })).toBe('2026-07-20');
  });

  it('uses the creation date for a Diverse job without a report date', () => {
    expect(getJobListReportDate({
      jobType: 'Diverse',
      reportDate: null,
      createdAt: '2026-07-31T10:00:00Z',
    })).toBe('2026-07-31T10:00:00Z');
  });

  it('does not invent a report date for other job types', () => {
    expect(getJobListReportDate({
      jobType: 'KLS',
      reportDate: null,
      createdAt: '2026-07-31T10:00:00Z',
    })).toBeNull();
  });
});
