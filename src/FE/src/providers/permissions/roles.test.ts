import { describe, expect, it } from 'vitest';
import { ROLES, canReceiveJobAssignment } from './roles';

describe('canReceiveJobAssignment', () => {
  it('allows employees only', () => {
    expect(canReceiveJobAssignment(ROLES.User)).toBe(true);
    expect(canReceiveJobAssignment(ROLES.Admin)).toBe(false);
    expect(canReceiveJobAssignment(ROLES.Auditor)).toBe(false);
    expect(canReceiveJobAssignment(ROLES.Superadmin)).toBe(false);
  });
});
