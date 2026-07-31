import { describe, expect, it } from 'vitest';
import { ROLES } from './permissions';
import {
  AUDITOR_AUTHENTICATED_PATH,
  DEFAULT_AUTHENTICATED_PATH,
  getAuthenticatedHomePath,
  resolveAuthenticatedReturnTo,
} from '../features/auth/authenticatedDestination';

describe('role destination', () => {
  it('uses reports for the Auditor role', () => {
    expect(getAuthenticatedHomePath(ROLES.Auditor)).toBe(AUDITOR_AUTHENTICATED_PATH);
  });

  it.each([ROLES.User, ROLES.Admin, ROLES.Superadmin])('keeps %s on the standard home', (role) => {
    expect(getAuthenticatedHomePath(role)).toBe(DEFAULT_AUTHENTICATED_PATH);
  });

  it('preserves an explicit application path', () => {
    expect(resolveAuthenticatedReturnTo('/app/completed/job-id', ROLES.Auditor))
      .toBe('/app/completed/job-id');
  });
});
