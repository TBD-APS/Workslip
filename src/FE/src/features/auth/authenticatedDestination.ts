import { hasRole, ROLES } from '../../providers/permissions';

export const DEFAULT_AUTHENTICATED_PATH = '/app';
export const AUDITOR_AUTHENTICATED_PATH = '/app/auditor';

export function getAuthenticatedHomePath(role: string | null | undefined): string {
  return hasRole(role, ROLES.Auditor)
    ? AUDITOR_AUTHENTICATED_PATH
    : DEFAULT_AUTHENTICATED_PATH;
}

export function resolveAuthenticatedReturnTo(
  returnTo: string,
  role: string | null | undefined,
): string {
  return returnTo === DEFAULT_AUTHENTICATED_PATH
    ? getAuthenticatedHomePath(role)
    : returnTo;
}
