/**
 * Provides the tenant module-access context.
 *
 * Defaults to the `'all'` sentinel (nothing hidden). Once the product-owned
 * adapter delivers the effective-capability summary in the session, mount this
 * with the resolved set. Server-side enforcement (IWorkslipModuleAccess) remains
 * the authority — this only drives navigation/affordance UX.
 */

import { useMemo, type ReactNode } from 'react';
import { ModuleAccessContext, type ModuleAccessValue } from './ModuleAccessContext';
import { isModuleEnabled, type EnabledModules } from './moduleAccess';

type ModuleAccessProviderProps = {
  children: ReactNode;
  /** Effective entitled modules. Defaults to `'all'` until the adapter feeds it. */
  enabled?: EnabledModules;
  isLoading?: boolean;
};

export function ModuleAccessProvider({
  children,
  enabled = 'all',
  isLoading = false,
}: ModuleAccessProviderProps) {
  const value = useMemo<ModuleAccessValue>(
    () => ({ enabled, isLoading, isModuleEnabled: (key) => isModuleEnabled(enabled, key) }),
    [enabled, isLoading],
  );

  return <ModuleAccessContext.Provider value={value}>{children}</ModuleAccessContext.Provider>;
}
