/**
 * <RoleGuard roles={...}> — route-level guard.
 *
 * Wrap any route element to gate it on role membership. While the user is
 * loading, renders a small inline loading state. Unauthenticated users are
 * redirected to /login by the outer `ProtectedRoute`; RoleGuard only handles
 * role-based access.
 *
 * Use the `permission` prop to gate on a specific permission instead of a
 * raw role list — preferred, because it survives role reshuffles.
 */

import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../AuthContext';
import { hasAnyRole, type Role } from './roles';
import { hasPermission, type Permission } from './permissions';

type RoleGuardProps = {
  children: ReactNode;
  roles?: readonly Role[];
  permission?: Permission;
  /** Where to send users who fail the check. Defaults to /app. */
  redirectTo?: string;
};

export function RoleGuard({ children, roles, permission, redirectTo = '/app' }: RoleGuardProps) {
  const { user, isLoading } = useAuth();

  if (isLoading) {
    return (
      <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--text-secondary)' }}>
        Tjekker adgang...
      </div>
    );
  }

  const role = user?.role ?? null;
  const allowedByRoles = roles ? hasAnyRole(role, roles) : true;
  const allowedByPermission = permission ? hasPermission(role, permission) : true;

  if (!allowedByRoles || !allowedByPermission) {
    return <Navigate to={redirectTo} replace />;
  }

  return <>{children}</>;
}
