/**
 * <Can permission="..."> — render children only when the current user
 * has the given permission.
 *
 * - Renders nothing while the user is loading (avoids flicker / false denies).
 * - `fallback` is rendered when the user is loaded but lacks the permission.
 * - `disableInstead` swaps the children for a `disabled` clone — useful when
 *   you want the affordance to stay visible but inert (e.g. a button with
 *   a "no permission" tooltip).
 */

import type { ReactNode } from 'react';
import { useAuth } from '../AuthContext';
import { hasPermission, type Permission } from './permissions';

type CanProps = {
  permission: Permission;
  children: ReactNode;
  fallback?: ReactNode;
};

export function Can({ permission, children, fallback = null }: CanProps) {
  const { user, isLoading } = useAuth();

  if (isLoading) return null;
  if (!hasPermission(user?.role, permission)) return <>{fallback}</>;
  return <>{children}</>;
}
