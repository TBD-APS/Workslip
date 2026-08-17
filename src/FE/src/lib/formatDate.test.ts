import { describe, expect, it } from 'vitest';
import { formatDate, formatDateLong, formatDateShort } from './formatDate';

describe('formatDate', () => {
  it('formats user-visible dates using the canonical Danish presentation', () => {
    expect(formatDate('2026-08-17T12:00:00Z')).toBe('17. aug. 2026');
  });

  it('keeps legacy date-only helpers on the same global presentation contract', () => {
    expect(formatDateLong('2026-08-17T12:00:00Z')).toBe('17. aug. 2026');
    expect(formatDateShort('2026-08-17T12:00:00Z')).toBe('17. aug. 2026');
  });

  it('preserves the existing null and invalid-value contract', () => {
    expect(formatDate(null)).toBeNull();
    expect(formatDate(undefined)).toBeNull();
    expect(formatDate('not-a-date')).toBe('not-a-date');
  });
});
