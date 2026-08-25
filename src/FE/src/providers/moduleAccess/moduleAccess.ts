/**
 * Pure module-access resolution, kept free of React so it is trivially testable.
 *
 * `enabled` is the tenant's effective set of entitled modules, as projected by
 * the product-owned adapter and delivered in the session summary. The sentinel
 * `'all'` is the interim default used before the entitlement projection is wired
 * — it keeps every capability visible so nothing is hidden by accident.
 */

import { ALWAYS_ON_MODULES, type ModuleKey } from './moduleKeys';

export type EnabledModules = ReadonlySet<ModuleKey> | 'all';

export function isModuleEnabled(enabled: EnabledModules, key: ModuleKey): boolean {
  if (ALWAYS_ON_MODULES.includes(key)) return true;
  if (enabled === 'all') return true;
  return enabled.has(key);
}
