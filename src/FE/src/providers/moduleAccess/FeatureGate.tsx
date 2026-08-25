/**
 * <FeatureGate module="..."> — render children only when the current tenant is
 * entitled to the given module.
 *
 * Sibling to <Can permission="...">: FeatureGate answers the *tenant entitlement*
 * question, Can answers the *user permission* question. A capability that is both
 * sold to the tenant and permitted for the user wraps both:
 *
 *   <FeatureGate module="time-economics"><Can permission="worksheet:view">…</Can></FeatureGate>
 *
 * - Renders nothing while the entitlement summary is loading (avoids flicker).
 * - `fallback` is rendered when loaded but the module is not entitled.
 *
 * This is UX gating only. Every protected endpoint, worker, file op and export
 * must still enforce the same decision server-side via IWorkslipModuleAccess.
 */

import type { ReactNode } from 'react';
import { useModuleAccess } from './useModuleAccess';
import type { ModuleKey } from './moduleKeys';

type FeatureGateProps = {
  module: ModuleKey;
  children: ReactNode;
  fallback?: ReactNode;
};

export function FeatureGate({ module, children, fallback = null }: FeatureGateProps) {
  const { isLoading, isModuleEnabled } = useModuleAccess();

  if (isLoading) return null;
  if (!isModuleEnabled(module)) return <>{fallback}</>;
  return <>{children}</>;
}
