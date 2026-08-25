import { describe, expect, it } from 'vitest';
import { isModuleEnabled } from './moduleAccess';

describe('isModuleEnabled', () => {
  it('treats always-on foundation as enabled regardless of the set', () => {
    expect(isModuleEnabled(new Set(), 'foundation')).toBe(true);
  });

  it('enables everything under the interim "all" sentinel', () => {
    expect(isModuleEnabled('all', 'compliance-evidence')).toBe(true);
  });

  it('honours an explicit entitlement set', () => {
    const enabled = new Set(['work-management'] as const);
    expect(isModuleEnabled(enabled, 'work-management')).toBe(true);
    expect(isModuleEnabled(enabled, 'insights-exports')).toBe(false);
  });
});
