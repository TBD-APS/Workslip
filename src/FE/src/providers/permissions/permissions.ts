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
  | 'job:assign'
  | 'job:delete'
  | 'job:viewAll'
  | 'worksheet:assign'
  | 'worksheet:view'
  | 'user:manage'
  | 'report:view';

const ADMIN_ROLES: readonly Role[] = [ROLES.Admin, ROLES.Superadmin];
const AUDITOR_ROLES: readonly Role[] = [ROLES.Auditor, ROLES.Admin, ROLES.Superadmin];
const USER_ROLES: readonly Role[] = [ROLES.User, ROLES.Admin, ROLES.Superadmin];

const PERMISSIONS: Record<Permission, readonly Role[]> = {
  'job:create': ADMIN_ROLES,
  'job:assign': ADMIN_ROLES,
  'job:delete': ADMIN_ROLES,
  'job:viewAll': ADMIN_ROLES,
  'worksheet:assign': ADMIN_ROLES,
  'worksheet:view': USER_ROLES,
  'user:manage': ADMIN_ROLES,
  'report:view': AUDITOR_ROLES,
};

export function hasPermission(role: string | null | undefined, permission: Permission): boolean {
  const r = normalizeRole(role);
  if (!r) return false;
  const allowed = PERMISSIONS[permission];
  return hasAnyRole(r, allowed);
}
