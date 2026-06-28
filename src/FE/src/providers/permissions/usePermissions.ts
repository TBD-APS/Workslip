/**
 * React hooks over the role / permission model.
 *
 * The `useCan` / `useHasRole` / `useIsAdmin` hooks read the current user
 * from `AuthContext` and return a boolean. They also expose the role so
 * callers can branch on it.
 *
 * `isAuthLoading` is exposed separately so permission-aware UIs can render
 * a skeleton while the `/api/auth/me` request is in-flight.
 */

import { useAuth } from '../useAuth';
import { hasAnyRole, hasRole, isRoleAtLeast, normalizeRole, ROLES, type Role } from './roles';
import { hasPermission, type Permission } from './permissions';

export function useCurrentRole(): Role | null {
  const { user } = useAuth();
  return normalizeRole(user?.role);
}

export function useHasRole(allowed: Role | readonly Role[]): boolean {
  const { user } = useAuth();
  const list = Array.isArray(allowed) ? allowed : [allowed as Role];
  return hasAnyRole(user?.role, list);
}

export function useIsAdmin(): boolean {
  const { user } = useAuth();
  return isRoleAtLeast(user?.role, ROLES.Admin);
}

export function useIsAuditor(): boolean {
  const { user } = useAuth();
  return isRoleAtLeast(user?.role, ROLES.Auditor);
}

export function useIsSuperAdmin(): boolean {
  const { user } = useAuth();
  return hasRole(user?.role, ROLES.Superadmin);
}

export function useCan(permission: Permission): boolean {
  const { user } = useAuth();
  return hasPermission(user?.role, permission);
}
