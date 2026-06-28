/**
 * Canonical role identifiers and hierarchy helpers.
 *
 * Roles are sourced from the BE (`Workslip.Infrastructure/.../UserRequestValidator`).
 * Roles may arrive as "Superadmin" (validator output) or "SuperAdmin" (direct seed)
 * — comparisons are case-insensitive and use the canonical strings below.
 *
 * Hierarchy (low → high):
 *   User        — standard user, restricted actions
 *   Auditor     — read-only access to completed reports
 *   Admin       — full app functionality (current default)
 *   Superadmin  — system admin (reserved for future use)
 *
 * Extend by adding a new entry to ROLES, ROLE_RANK, and any new helpers
 * (e.g. role-specific overrides). Permission checks live in `permissions.ts`,
 * not here.
 */

export const ROLES = {
  User: 'User',
  Auditor: 'Auditor',
  Admin: 'Admin',
  Superadmin: 'Superadmin',
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

const ROLE_RANK: Record<Role, number> = {
  [ROLES.User]: 1,
  [ROLES.Auditor]: 2,
  [ROLES.Admin]: 3,
  [ROLES.Superadmin]: 4,
};

export function normalizeRole(role: string | null | undefined): Role | null {
  if (!role) return null;
  const r = role.trim().toLowerCase();
  if (r === 'user') return ROLES.User;
  if (r === 'auditor') return ROLES.Auditor;
  if (r === 'admin') return ROLES.Admin;
  if (r === 'superadmin') return ROLES.Superadmin;
  return null;
}

/**
 * True if the user's role is at or above the required minimum in the hierarchy.
 * Default-deny: a null/unknown role can never satisfy any check.
 */
export function isRoleAtLeast(role: string | null | undefined, min: Role): boolean {
  const r = normalizeRole(role);
  if (!r) return false;
  return ROLE_RANK[r] >= ROLE_RANK[min];
}

export function hasAnyRole(role: string | null | undefined, allowed: readonly Role[]): boolean {
  const r = normalizeRole(role);
  if (!r) return false;
  return allowed.some((candidate) => normalizeRole(candidate) === r);
}

export function hasRole(role: string | null | undefined, expected: Role): boolean {
  return hasAnyRole(role, [expected]);
}
