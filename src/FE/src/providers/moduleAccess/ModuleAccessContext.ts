/**
 * Tenant module-access context value + default. Kept component-free so the
 * provider component lives on its own (Fast Refresh: one concern per file).
 */

import { createContext } from 'react';
import { isModuleEnabled, type EnabledModules } from './moduleAccess';
import type { ModuleKey } from './moduleKeys';

export type ModuleAccessValue = {
  /** Effective entitled modules, or the `'all'` interim sentinel. */
  enabled: EnabledModules;
  /** True while the session/entitlement summary is still loading. */
  isLoading: boolean;
  isModuleEnabled: (key: ModuleKey) => boolean;
};

/** Safe default: nothing hidden, used until a provider feeds the resolved set. */
export const DEFAULT_MODULE_ACCESS: ModuleAccessValue = {
  enabled: 'all',
  isLoading: false,
  isModuleEnabled: (key) => isModuleEnabled('all', key),
};

export const ModuleAccessContext = createContext<ModuleAccessValue>(DEFAULT_MODULE_ACCESS);
