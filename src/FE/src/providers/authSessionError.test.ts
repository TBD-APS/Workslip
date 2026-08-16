import { describe, expect, it } from 'vitest';
import { isRejectedAuthSession, shouldRetryAuthSession } from './authSessionError';

const axiosError = (status?: number): unknown => ({
  isAxiosError: true,
  response: status === undefined ? undefined : { status },
});

describe('auth session failure policy', () => {
  it('treats only HTTP 401 as an authoritative session rejection', () => {
    expect(isRejectedAuthSession(axiosError(401))).toBe(true);
    expect(isRejectedAuthSession(axiosError(403))).toBe(false);
    expect(isRejectedAuthSession(axiosError(500))).toBe(false);
    expect(isRejectedAuthSession(axiosError())).toBe(false);
    expect(isRejectedAuthSession(new Error('network'))).toBe(false);
  });

  it('does not retry an authoritative 401', () => {
    expect(shouldRetryAuthSession(0, axiosError(401))).toBe(false);
  });

  it('preserves the existing single retry for transient failures', () => {
    expect(shouldRetryAuthSession(0, axiosError(503))).toBe(true);
    expect(shouldRetryAuthSession(1, axiosError(503))).toBe(false);
    expect(shouldRetryAuthSession(0, axiosError())).toBe(true);
    expect(shouldRetryAuthSession(1, axiosError())).toBe(false);
  });
});
