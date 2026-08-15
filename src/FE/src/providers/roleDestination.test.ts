import { describe, expect, it } from 'vitest';
import { ROLES } from './permissions';
import {
  AUDITOR_AUTHENTICATED_PATH,
  DEFAULT_AUTHENTICATED_PATH,
  getAuthenticatedHomePath,
} from '../features/auth/authenticatedDestination';

describe('role destination', () => {
  it('uses Overblik as the standard authenticated landing page', () => {
    expect(DEFAULT_AUTHENTICATED_PATH).toBe('/app/overblik');
  });

  it('uses reports for the Auditor role', () => {
    expect(getAuthenticatedHomePath(ROLES.Auditor)).toBe(AUDITOR_AUTHENTICATED_PATH);
  });

  it.each([ROLES.User, ROLES.Admin, ROLES.Superadmin])('keeps %s on the standard home', (role) => {
    expect(getAuthenticatedHomePath(role)).toBe(DEFAULT_AUTHENTICATED_PATH);
  });
});
