import { describe, expect, it } from 'vitest';
import { isJobFamilyQueryKey } from './jobQueryKeys';

describe('isJobFamilyQueryKey', () => {
  it('matches the job list family', () => {
    expect(isJobFamilyQueryKey(['/api/jobs'])).toBe(true);
    expect(isJobFamilyQueryKey(['/api/jobs', { status: ['Draft'] }])).toBe(true);
    expect(isJobFamilyQueryKey(['/api/jobs', { status: ['Draft'] }, { search: 'x', sort: 'n', limit: 20 }])).toBe(true);
  });

  it('matches single-job detail queries', () => {
    expect(isJobFamilyQueryKey(['/api/jobs/job-1'])).toBe(true);
    expect(isJobFamilyQueryKey(['/api/jobs/job-1/status'])).toBe(true);
  });

  it('rejects unrelated query keys', () => {
    expect(isJobFamilyQueryKey(['/api/users'])).toBe(false);
    expect(isJobFamilyQueryKey(['/api/jobs-list'])).toBe(false);
    expect(isJobFamilyQueryKey(['/api/job'])).toBe(false);
    expect(isJobFamilyQueryKey([])).toBe(false);
    expect(isJobFamilyQueryKey([{ status: ['Draft'] }])).toBe(false);
  });
});
