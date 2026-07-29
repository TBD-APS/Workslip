export interface EntraTokenPayload {
  access_token?: string;
  id_token?: string;
  error?: string;
  error_description?: string;
}

export interface EntraTokenExchangeResult {
  accessToken: string;
  logoutHint: string | null;
}

const REQUIRED_OIDC_SCOPES = ['openid', 'profile'] as const;

export const ensureOidcScopes = (configuredScope: string): string => {
  const scopes = configuredScope.split(/\s+/).filter(Boolean);
  const uniqueScopes = new Set(scopes);

  for (const requiredScope of REQUIRED_OIDC_SCOPES) {
    uniqueScopes.add(requiredScope);
  }

  return Array.from(uniqueScopes).join(' ');
};

export const extractEntraLogoutHint = (idToken: string | undefined): string | null => {
  if (!idToken) return null;

  const segments = idToken.split('.');
  if (segments.length < 2) return null;

  try {
    const base64 = segments[1].replace(/-/g, '+').replace(/_/g, '/');
    const paddingLength = (4 - (base64.length % 4)) % 4;
    const binary = atob(base64.padEnd(base64.length + paddingLength, '='));
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    const claims = JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;
    const loginHint = claims.login_hint;

    return typeof loginHint === 'string' && loginHint.length > 0 ? loginHint : null;
  } catch {
    return null;
  }
};
