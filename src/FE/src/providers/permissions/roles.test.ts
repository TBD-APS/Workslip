import { describe, expect, it } from 'vitest';
import { ROLES, canReceiveJobAssignment } from './roles';

describe('canReceiveJobAssignment', () => {
  it('allows user and admin targets', () => {
    expect(canReceiveJobAssignment(ROLES.User)).toBe(true);
    expect(canReceiveJobAssignment(ROLES.Admin)).toBe(true);
  });

  it('rejects auditor and superadmin targets', () => {
    expect(canReceiveJobAssignment(ROLES.Auditor)).toBe(false);
    expect(canReceiveJobAssignment(ROLES.Superadmin)).toBe(false);
  });
});
