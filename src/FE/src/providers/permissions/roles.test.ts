import { describe, expect, it } from 'vitest';
import { ROLES, canReceiveJobAssignment } from './roles';

describe('canReceiveJobAssignment', () => {
  it('allows employees regardless of whether they are the current user', () => {
    expect(canReceiveJobAssignment(ROLES.User)).toBe(true);
    expect(canReceiveJobAssignment(ROLES.User, true)).toBe(true);
  });

  it('allows an admin only when the candidate is the current user', () => {
    expect(canReceiveJobAssignment(ROLES.Admin, true)).toBe(true);
    expect(canReceiveJobAssignment(ROLES.Admin, false)).toBe(false);
  });

  it('rejects auditor and superadmin targets', () => {
    expect(canReceiveJobAssignment(ROLES.Auditor, true)).toBe(false);
    expect(canReceiveJobAssignment(ROLES.Superadmin, true)).toBe(false);
  });
});
