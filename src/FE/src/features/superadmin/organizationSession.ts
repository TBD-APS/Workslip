import {
  AUTH_TOKEN_KEY,
  AuthStorage,
} from '../../providers/authContextValue';

const HOME_AUTH_TOKEN_KEY = 'workslip.superadmin.homeAuthToken';
const ORGANIZATION_SESSION_ID_KEY = 'workslip.superadmin.organizationSessionId';
const ORGANIZATION_SESSION_NAME_KEY = 'workslip.superadmin.organizationSessionName';

export interface OrganizationSession {
  id: string;
  name: string;
}

export function getOrganizationSession(): OrganizationSession | null {
  const id = AuthStorage.getItem(ORGANIZATION_SESSION_ID_KEY)?.trim();
  const homeToken = AuthStorage.getItem(HOME_AUTH_TOKEN_KEY);
  if (!id || !homeToken) return null;

  return {
    id,
    name: AuthStorage.getItem(ORGANIZATION_SESSION_NAME_KEY)?.trim() || 'Valgt organisation',
  };
}

export function activateOrganizationSession(
  organization: OrganizationSession,
  delegatedToken: string,
): void {
  const currentToken = AuthStorage.getItem(AUTH_TOKEN_KEY);
  if (!currentToken) {
    throw new Error('Der mangler en aktiv Superadmin-session.');
  }

  // Preserve the original token across organization switches. A delegated
  // token must never replace the home token used to exit the session.
  if (!AuthStorage.getItem(HOME_AUTH_TOKEN_KEY)) {
    AuthStorage.setItem(HOME_AUTH_TOKEN_KEY, currentToken);
  }

  AuthStorage.setItem(ORGANIZATION_SESSION_ID_KEY, organization.id);
  AuthStorage.setItem(ORGANIZATION_SESSION_NAME_KEY, organization.name);
  AuthStorage.setItem(AUTH_TOKEN_KEY, delegatedToken);
}

export function restoreHomeOrganizationSession(): boolean {
  const homeToken = AuthStorage.getItem(HOME_AUTH_TOKEN_KEY);
  clearOrganizationSession();

  if (!homeToken) return false;

  AuthStorage.setItem(AUTH_TOKEN_KEY, homeToken);
  return true;
}

export function clearOrganizationSession(): void {
  AuthStorage.removeItem(HOME_AUTH_TOKEN_KEY);
  AuthStorage.removeItem(ORGANIZATION_SESSION_ID_KEY);
  AuthStorage.removeItem(ORGANIZATION_SESSION_NAME_KEY);
}
