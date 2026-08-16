import { hasRole, ROLES } from '../../providers/permissions';

export const DEFAULT_AUTHENTICATED_PATH = '/app/overblik';
export const AUDITOR_AUTHENTICATED_PATH = '/app/auditor';
export const SUPERADMIN_AUTHENTICATED_PATH = '/superadmin';

export function getAuthenticatedHomePath(role: string | null | undefined): string {
  if (hasRole(role, ROLES.Superadmin)) return SUPERADMIN_AUTHENTICATED_PATH;
  if (hasRole(role, ROLES.Auditor)) return AUDITOR_AUTHENTICATED_PATH;
  return DEFAULT_AUTHENTICATED_PATH;
}
