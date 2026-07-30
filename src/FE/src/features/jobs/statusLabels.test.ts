import { describe, expect, it } from 'vitest';
import { JobStatus } from '../../api/generated/models/jobStatus';
import { formatJobStatus } from './statusLabels';

describe('formatJobStatus', () => {
  it.each([
    [JobStatus.Draft, 'Aktiv'],
    [JobStatus.InReview, 'Til gennemsyn'],
    [JobStatus.Approved, 'Godkendt'],
    [JobStatus.Rejected, 'Afvist'],
  ])('formats %s as %s', (status, expected) => {
    expect(formatJobStatus(status)).toBe(expected);
  });

  it('keeps an unknown status unchanged', () => {
    expect(formatJobStatus('FutureStatus')).toBe('FutureStatus');
  });
});
