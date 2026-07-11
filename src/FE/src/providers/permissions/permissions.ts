/**
 * Permission catalogue.
 *
 * Each permission is a string in the form "<domain>:<action>".
 * Add new permissions here and the corresponding role allow-list, then
 * gate UI / behaviour via `useCan()` or `<Can permission="..." />`.
 *
 * The matrix is the single source of truth for "which role can do what" —
 * never check roles directly in components.
 */

import { ROLES, type Role } from './roles';
import { hasAnyRole, normalizeRole } from './roles';

export type Permission =
  | 'job:create'
  | 'job-from-customer:create'
  | 'job:assign'
  | 'job:delete'
  | 'job:viewAll'
  | 'worksheet:assign'
  | 'worksheet:view'
  | 'user:manage'
  | 'report:view'
  | 'customer:view'
  | 'customer:edit'

const ADMIN_PERMISSIONS: readonly Role[] = [ROLES.Admin, ROLES.Superadmin];
const CUSTOMER_PERMISSIONS: readonly Role[] = [ROLES.User, ROLES.Admin, ROLES.Superadmin];
const AUDITOR_PERMISSIONS: readonly Role[] = [ROLES.Auditor, ROLES.Admin, ROLES.Superadmin];
const USER_PERMISSIONS: readonly Role[] = [ROLES.User, ROLES.Admin, ROLES.Superadmin];

const PERMISSIONS: Record<Permission, readonly Role[]> = {
  'job:create': ADMIN_PERMISSIONS,
  'job-from-customer:create': USER_PERMISSIONS,
  'job:assign': ADMIN_PERMISSIONS,
  'job:delete': ADMIN_PERMISSIONS,
  'job:viewAll': ADMIN_PERMISSIONS,
  'worksheet:assign': ADMIN_PERMISSIONS,
  'worksheet:view': USER_PERMISSIONS,
  'user:manage': ADMIN_PERMISSIONS,
  'report:view': AUDITOR_PERMISSIONS,
  'customer:view': CUSTOMER_PERMISSIONS,
  'customer:edit': ADMIN_PERMISSIONS
};

export function hasPermission(role: string | null | undefined, permission: Permission): boolean {
  const r = normalizeRole(role);
  if (!r) return false;
  const allowed = PERMISSIONS[permission];
  return hasAnyRole(r, allowed);
}
