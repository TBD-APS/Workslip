import { describe, expect, it } from 'vitest';
import { ROLES } from './permissions';
import { canUseSessionNotifications } from './sessionFeaturePolicy';

describe('role feature policy', () => {
  it('excludes roles without access', () => {
    expect(canUseSessionNotifications(ROLES.Auditor)).toBe(false);
  });

  it('includes supported roles', () => {
    expect(canUseSessionNotifications(ROLES.User)).toBe(true);
    expect(canUseSessionNotifications(ROLES.Admin)).toBe(true);
    expect(canUseSessionNotifications(ROLES.Superadmin)).toBe(true);
  });
});
