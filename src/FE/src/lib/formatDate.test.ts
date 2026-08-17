import { describe, expect, it } from 'vitest';
import { formatDateShort } from './formatDate';

describe('formatDateShort', () => {
  it('formats valid dates using the Danish short date format', () => {
    expect(formatDateShort('2026-08-17T12:00:00Z')).toBe('17.08.2026');
  });

  it('preserves the existing null and invalid-value contract', () => {
    expect(formatDateShort(null)).toBeNull();
    expect(formatDateShort(undefined)).toBeNull();
    expect(formatDateShort('not-a-date')).toBe('not-a-date');
  });
});
