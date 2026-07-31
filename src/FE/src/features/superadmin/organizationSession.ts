import {
  AUTH_TOKEN_KEY,
  AuthStorage,
  REAUTH_IN_FLIGHT_KEY,
  USER_EMAIL_KEY,
} from '../../providers/authContextValue';

const HOME_AUTH_TOKEN_KEY = 'workslip.superadmin.homeAuthToken';
const ORGANIZATION_SESSION_ID_KEY = 'workslip.superadmin.organizationSessionId';
const ORGANIZATION_SESSION_NAME_KEY = 'workslip.superadmin.organizationSessionName';
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
const NAME_IDENTIFIER_CLAIMS = [
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier',
  'nameid',
  'sub',
] as const;
const ORGANIZATION_ID_CLAIM = 'organizationId';
const HOME_ORGANIZATION_ID_CLAIM = 'homeOrganizationId';
const DELEGATED_ORGANIZATION_SESSION_CLAIM = 'delegatedOrganizationSession';
const UUID_PATTERN = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export interface OrganizationSession {
  id: string;
  name: string;
}

interface StoredOrganizationSessionState {
  activeToken: string | null;
  homeToken: string | null;
  organizationId: string | null;
  organizationName: string | null;
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

export function isSuperadminAuthToken(): boolean {
  const payload = readTokenPayload(AuthStorage.getItem(AUTH_TOKEN_KEY));
  return payload !== null && isSuperadminPayload(payload);
}

export function isDelegatedOrganizationSessionToken(token: string | null): boolean {
  const payload = readTokenPayload(token);
  return payload !== null && readDelegatedClaim(payload) === true;
}

export function activateOrganizationSession(
  organization: OrganizationSession,
  delegatedToken: string,
): void {
  const currentToken = AuthStorage.getItem(AUTH_TOKEN_KEY);
  const savedHomeToken = AuthStorage.getItem(HOME_AUTH_TOKEN_KEY);
  const homeToken = savedHomeToken ?? currentToken;
  if (!currentToken || !homeToken) {
    throw new Error('Der mangler en aktiv Superadmin-session.');
  }

  const delegatedPayload = readTokenPayload(delegatedToken);
  const homePayload = readTokenPayload(homeToken);
  const currentPayload = readTokenPayload(currentToken);
  const delegatedOrganizationId = delegatedPayload
    ? normalizeUuid(readStringClaim(delegatedPayload, ORGANIZATION_ID_CLAIM))
    : null;
  const selectedOrganizationId = normalizeUuid(organization.id);
  if (
    !delegatedPayload
    || !homePayload
    || !currentPayload
    || !isUnexpiredPayload(delegatedPayload)
    || !isValidRecoveryPair(delegatedPayload, homePayload)
    || !delegatedOrganizationId
    || !selectedOrganizationId
    || delegatedOrganizationId !== selectedOrganizationId
    || (
      currentToken !== homeToken
      && (
        !isValidRecoveryPair(currentPayload, homePayload)
        || !storedOrganizationMatchesPayload(
          AuthStorage.getItem(ORGANIZATION_SESSION_ID_KEY),
          currentPayload,
        )
      )
    )
  ) {
    throw new Error('Organisationssessionens token kunne ikke valideres.');
  }

  // Preserve the original token across organization switches. A delegated
  // token must never replace the home token used to exit the session.
  if (!savedHomeToken) {
    AuthStorage.setItem(HOME_AUTH_TOKEN_KEY, currentToken);
  }

  AuthStorage.setItem(ORGANIZATION_SESSION_ID_KEY, organization.id);
  AuthStorage.setItem(ORGANIZATION_SESSION_NAME_KEY, organization.name);
  AuthStorage.setItem(AUTH_TOKEN_KEY, delegatedToken);
}

export function restoreHomeOrganizationSession(): boolean {
  while (true) {
    const snapshot = readStoredState();
    const delegatedPayload = readTokenPayload(snapshot.activeToken);
    const homePayload = readTokenPayload(snapshot.homeToken);
    const canRestore = Boolean(
      delegatedPayload
      && homePayload
      && isValidRecoveryPair(delegatedPayload, homePayload)
      && storedOrganizationMatchesPayload(snapshot.organizationId, delegatedPayload),
    );

    if (!storedStateMatches(snapshot)) continue;

    if (!canRestore) {
      clearAuthenticationAndOrganizationSession();
      return false;
    }

    AuthStorage.setItem(AUTH_TOKEN_KEY, snapshot.homeToken!);
    AuthStorage.removeItem(REAUTH_IN_FLIGHT_KEY);
    clearOrganizationSession();
    return true;
  }
}

export function clearOrganizationSession(): void {
  AuthStorage.removeItem(HOME_AUTH_TOKEN_KEY);
  AuthStorage.removeItem(ORGANIZATION_SESSION_ID_KEY);
  AuthStorage.removeItem(ORGANIZATION_SESSION_NAME_KEY);
}

function isValidRecoveryPair(
  delegatedPayload: Record<string, unknown>,
  homePayload: Record<string, unknown>,
): boolean {
  if (
    !hasNumericExpiry(delegatedPayload)
    || !isUnexpiredPayload(homePayload)
    || readDelegatedClaim(delegatedPayload) !== true
    || !hasValidHomeDelegationClaim(homePayload)
    || !isSuperadminPayload(delegatedPayload)
    || !isSuperadminPayload(homePayload)
  ) {
    return false;
  }

  const delegatedActorId = readActorId(delegatedPayload);
  const homeActorId = readActorId(homePayload);
  const delegatedHomeOrganizationId = normalizeUuid(
    readStringClaim(delegatedPayload, HOME_ORGANIZATION_ID_CLAIM),
  );
  const homeOrganizationId = normalizeUuid(
    readStringClaim(homePayload, ORGANIZATION_ID_CLAIM),
  );

  return delegatedActorId !== null
    && delegatedActorId === homeActorId
    && delegatedHomeOrganizationId !== null
    && delegatedHomeOrganizationId === homeOrganizationId;
}

function isSuperadminPayload(payload: Record<string, unknown>): boolean {
  return readRole(payload)?.toLowerCase() === 'superadmin';
}

function readRole(payload: Record<string, unknown>): string | null {
  const rawRole = payload.role ?? payload.roles ?? payload[ROLE_CLAIM];
  const role = Array.isArray(rawRole) ? rawRole[0] : rawRole;
  return typeof role === 'string' && role.trim() ? role.trim() : null;
}

function isUnexpiredPayload(payload: Record<string, unknown>): boolean {
  return hasNumericExpiry(payload) && payload.exp > Date.now() / 1000;
}

function hasNumericExpiry(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & { exp: number } {
  return typeof payload.exp === 'number' && Number.isFinite(payload.exp);
}

function readDelegatedClaim(payload: Record<string, unknown>): boolean | null {
  const value = payload[DELEGATED_ORGANIZATION_SESSION_CLAIM];
  if (value === false || value === 'false') return false;
  if (value === true || value === 'true') return true;
  return null;
}

function hasValidHomeDelegationClaim(
  payload: Record<string, unknown>,
): boolean {
  const value = payload[DELEGATED_ORGANIZATION_SESSION_CLAIM];
  return value === undefined || value === false || value === 'false';
}

function readActorId(payload: Record<string, unknown>): string | null {
  const actorIds: string[] = [];

  for (const claim of NAME_IDENTIFIER_CLAIMS) {
    if (payload[claim] === undefined) continue;

    const actorId = normalizeUuid(readStringClaim(payload, claim));
    if (!actorId) return null;
    actorIds.push(actorId);
  }

  return actorIds.length > 0
    && actorIds.every((actorId) => actorId === actorIds[0])
    ? actorIds[0]
    : null;
}

function readStringClaim(
  payload: Record<string, unknown>,
  claim: string,
): string | null {
  const value = payload[claim];
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

function normalizeUuid(value: string | null): string | null {
  return value && UUID_PATTERN.test(value) ? value.toLowerCase() : null;
}

function storedOrganizationMatchesPayload(
  storedOrganizationId: string | null,
  delegatedPayload: Record<string, unknown>,
): boolean {
  const normalizedStoredId = normalizeUuid(storedOrganizationId?.trim() || null);
  const delegatedOrganizationId = normalizeUuid(
    readStringClaim(delegatedPayload, ORGANIZATION_ID_CLAIM),
  );
  return normalizedStoredId !== null
    && delegatedOrganizationId !== null
    && normalizedStoredId === delegatedOrganizationId;
}

function readTokenPayload(token: string | null): Record<string, unknown> | null {
  if (!token) return null;

  try {
    const parts = token.split('.');
    if (parts.length !== 3 || parts.some((part) => !part)) return null;

    const normalized = parts[1]
      .replace(/-/g, '+')
      .replace(/_/g, '/')
      .padEnd(Math.ceil(parts[1].length / 4) * 4, '=');
    const parsed = JSON.parse(globalThis.atob(normalized)) as unknown;
    return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : null;
  } catch {
    return null;
  }
}

function readStoredState(): StoredOrganizationSessionState {
  return {
    activeToken: AuthStorage.getItem(AUTH_TOKEN_KEY),
    homeToken: AuthStorage.getItem(HOME_AUTH_TOKEN_KEY),
    organizationId: AuthStorage.getItem(ORGANIZATION_SESSION_ID_KEY),
    organizationName: AuthStorage.getItem(ORGANIZATION_SESSION_NAME_KEY),
  };
}

function storedStateMatches(expected: StoredOrganizationSessionState): boolean {
  const current = readStoredState();
  return current.activeToken === expected.activeToken
    && current.homeToken === expected.homeToken
    && current.organizationId === expected.organizationId
    && current.organizationName === expected.organizationName;
}

function clearAuthenticationAndOrganizationSession(): void {
  // Remove the potentially customer-scoped credential before its metadata.
  AuthStorage.removeItem(AUTH_TOKEN_KEY);
  AuthStorage.removeItem(USER_EMAIL_KEY);
  AuthStorage.removeItem(REAUTH_IN_FLIGHT_KEY);
  clearOrganizationSession();
}
