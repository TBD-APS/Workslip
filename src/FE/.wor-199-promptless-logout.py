from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file_path = Path(path)
    content = file_path.read_text(encoding="utf-8-sig")
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {count}:\n{old}")
    file_path.write_text(content.replace(old, new, 1), encoding="utf-8", newline="\n")


entra_oidc = Path("src/FE/src/features/auth/api/entraOidc.ts")
if entra_oidc.exists():
    raise RuntimeError(f"Refusing to overwrite existing file: {entra_oidc}")

entra_oidc.write_text(
    """export interface EntraTokenPayload {
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
  const scopes = configuredScope.split(/\\s+/).filter(Boolean);
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
""",
    encoding="utf-8",
    newline="\n",
)

entra_login = "src/FE/src/features/auth/api/entraLogin.ts"
replace_once(
    entra_login,
    "import type { AuthTokenResponse } from '../../../api/generated/models';\n",
    "import type { AuthTokenResponse } from '../../../api/generated/models';\nimport {\n  ensureOidcScopes,\n  extractEntraLogoutHint,\n  type EntraTokenExchangeResult,\n  type EntraTokenPayload,\n} from './entraOidc';\n",
)
replace_once(
    entra_login,
    "export interface CompleteEntraLoginResult {\n  auth: AuthTokenResponse;\n  returnTo: string;\n}",
    "export interface CompleteEntraLoginResult {\n  auth: AuthTokenResponse;\n  returnTo: string;\n  logoutHint: string | null;\n}",
)
replace_once(
    entra_login,
    "  const entraAccessToken = await exchangeCodeForToken(config, pkce, code);\n\n  const auth = await apiClient.post('/api/auth/entra-login', undefined, {\n    headers: {\n      Authorization: `Bearer ${entraAccessToken}`,\n    },\n  }) as unknown as AuthTokenResponse;\n\n  return { auth, returnTo: sanitizeReturnTo(pkce.returnTo) };",
    "  const tokenExchange = await exchangeCodeForToken(config, pkce, code);\n\n  const auth = await apiClient.post('/api/auth/entra-login', undefined, {\n    headers: {\n      Authorization: `Bearer ${tokenExchange.accessToken}`,\n    },\n  }) as unknown as AuthTokenResponse;\n\n  return {\n    auth,\n    returnTo: sanitizeReturnTo(pkce.returnTo),\n    logoutHint: tokenExchange.logoutHint,\n  };",
)
replace_once(
    entra_login,
    "export const buildEntraLogoutUrl = (tenantId: string, postLogoutRedirectUri: string): string => {\n  const logoutUrl = new URL(\n    `https://login.microsoftonline.com/${encodeURIComponent(tenantId)}/oauth2/v2.0/logout`,\n  );\n  logoutUrl.searchParams.set('post_logout_redirect_uri', postLogoutRedirectUri);\n  return logoutUrl.toString();\n};\n\nexport const startEntraLogout = () => {\n  const tenantId = getEntraTenantId();\n  clearEntraLoginSession();\n  window.location.replace(\n    buildEntraLogoutUrl(tenantId, `${window.location.origin}/login`),\n  );\n};",
    "export const buildEntraLogoutUrl = (\n  tenantId: string,\n  postLogoutRedirectUri: string,\n  logoutHint: string | null,\n): string => {\n  const logoutUrl = new URL(\n    `https://login.microsoftonline.com/${encodeURIComponent(tenantId)}/oauth2/v2.0/logout`,\n  );\n  logoutUrl.searchParams.set('post_logout_redirect_uri', postLogoutRedirectUri);\n  if (logoutHint) {\n    logoutUrl.searchParams.set('logout_hint', logoutHint);\n  }\n  return logoutUrl.toString();\n};\n\nexport const startEntraLogout = (logoutHint: string | null) => {\n  const tenantId = getEntraTenantId();\n  clearEntraLoginSession();\n  window.location.replace(\n    buildEntraLogoutUrl(tenantId, `${window.location.origin}/login`, logoutHint),\n  );\n};",
)
replace_once(
    entra_login,
    "  const scope = import.meta.env.VITE_AZURE_AD_SCOPE;\n  const redirectUri = import.meta.env.VITE_AZURE_AD_LOGIN_REDIRECT_URI;\n\n  if (!clientId || !scope) {\n    throw new Error('Microsoft login mangler konfiguration. Sæt VITE_AZURE_AD_TENANT_ID, VITE_AZURE_AD_CLIENT_ID og VITE_AZURE_AD_SCOPE.');\n  }\n\n  return { tenantId, clientId, scope, redirectUri };",
    "  const configuredScope = import.meta.env.VITE_AZURE_AD_SCOPE;\n  const redirectUri = import.meta.env.VITE_AZURE_AD_LOGIN_REDIRECT_URI;\n\n  if (!clientId || !configuredScope) {\n    throw new Error('Microsoft login mangler konfiguration. Sæt VITE_AZURE_AD_TENANT_ID, VITE_AZURE_AD_CLIENT_ID og VITE_AZURE_AD_SCOPE.');\n  }\n\n  return {\n    tenantId,\n    clientId,\n    scope: ensureOidcScopes(configuredScope),\n    redirectUri,\n  };",
)
replace_once(
    entra_login,
    "): Promise<string> => {",
    "): Promise<EntraTokenExchangeResult> => {",
)
replace_once(
    entra_login,
    "  const payload = await response.json().catch(() => ({} as Record<string, string>));",
    "  const payload = await response.json().catch(() => ({})) as EntraTokenPayload;",
)
replace_once(
    entra_login,
    "  return payload.access_token as string;",
    "  return {\n    accessToken: payload.access_token,\n    logoutHint: extractEntraLogoutHint(payload.id_token),\n  };",
)

invite_api = "src/FE/src/features/auth/api/entraInviteEnrollment.ts"
replace_once(
    invite_api,
    "import type { AuthTokenResponse } from '../../../api/generated/models';\n",
    "import type { AuthTokenResponse } from '../../../api/generated/models';\nimport {\n  ensureOidcScopes,\n  extractEntraLogoutHint,\n  type EntraTokenExchangeResult,\n  type EntraTokenPayload,\n} from './entraOidc';\n",
)
replace_once(
    invite_api,
    "export interface InviteEnrollmentDraft {\n  token: string;\n  displayName: string;\n  phone?: string;\n}\n",
    "export interface InviteEnrollmentDraft {\n  token: string;\n  displayName: string;\n  phone?: string;\n}\n\nexport interface CompleteEntraInviteEnrollmentResult {\n  auth: AuthTokenResponse;\n  logoutHint: string | null;\n}\n",
)
replace_once(
    invite_api,
    "export const completeEntraInviteEnrollment = async (): Promise<AuthTokenResponse> => {",
    "export const completeEntraInviteEnrollment = async (): Promise<CompleteEntraInviteEnrollmentResult> => {",
)
replace_once(
    invite_api,
    "  const entraAccessToken = await exchangeCodeForToken(config, pkce, code);\n\n  return apiClient.post('/api/auth/entra-enroll', {\n    token: draft.token,\n    displayName: draft.displayName,\n    phone: draft.phone,\n  }, {\n    headers: {\n      Authorization: `Bearer ${entraAccessToken}`,\n    },\n  });",
    "  const tokenExchange = await exchangeCodeForToken(config, pkce, code);\n\n  const auth = await apiClient.post('/api/auth/entra-enroll', {\n    token: draft.token,\n    displayName: draft.displayName,\n    phone: draft.phone,\n  }, {\n    headers: {\n      Authorization: `Bearer ${tokenExchange.accessToken}`,\n    },\n  }) as unknown as AuthTokenResponse;\n\n  return { auth, logoutHint: tokenExchange.logoutHint };",
)
replace_once(
    invite_api,
    "  const scope = import.meta.env.VITE_AZURE_AD_SCOPE;\n  const redirectUri = import.meta.env.VITE_AZURE_AD_REDIRECT_URI;\n\n  if (!tenantId || !clientId || !scope) {\n    throw new Error('Microsoft login mangler konfiguration. Sæt VITE_AZURE_AD_TENANT_ID, VITE_AZURE_AD_CLIENT_ID og VITE_AZURE_AD_SCOPE.');\n  }\n\n  return { tenantId, clientId, scope, redirectUri };",
    "  const configuredScope = import.meta.env.VITE_AZURE_AD_SCOPE;\n  const redirectUri = import.meta.env.VITE_AZURE_AD_REDIRECT_URI;\n\n  if (!tenantId || !clientId || !configuredScope) {\n    throw new Error('Microsoft login mangler konfiguration. Sæt VITE_AZURE_AD_TENANT_ID, VITE_AZURE_AD_CLIENT_ID og VITE_AZURE_AD_SCOPE.');\n  }\n\n  return {\n    tenantId,\n    clientId,\n    scope: ensureOidcScopes(configuredScope),\n    redirectUri,\n  };",
)
replace_once(
    invite_api,
    "): Promise<string> => {",
    "): Promise<EntraTokenExchangeResult> => {",
)
replace_once(
    invite_api,
    "  const payload = await response.json();",
    "  const payload = await response.json().catch(() => ({})) as EntraTokenPayload;",
)
replace_once(
    invite_api,
    "  return payload.access_token as string;",
    "  return {\n    accessToken: payload.access_token,\n    logoutHint: extractEntraLogoutHint(payload.id_token),\n  };",
)

auth_value = "src/FE/src/providers/authContextValue.ts"
replace_once(
    auth_value,
    "export const AUTH_PROVIDER_KEY = 'authProvider';\nexport const REAUTH_IN_FLIGHT_KEY",
    "export const AUTH_PROVIDER_KEY = 'authProvider';\nexport const ENTRA_LOGOUT_HINT_KEY = 'workslip.entraLogoutHint';\nexport const REAUTH_IN_FLIGHT_KEY",
)

auth_context = "src/FE/src/providers/AuthContext.tsx"
replace_once(
    auth_context,
    "  AUTH_PROVIDER_KEY,\n  AUTH_TOKEN_KEY,",
    "  AUTH_PROVIDER_KEY,\n  AUTH_TOKEN_KEY,\n  ENTRA_LOGOUT_HINT_KEY,",
)
replace_once(
    auth_context,
    "        AuthStorage.setItem(AUTH_PROVIDER_KEY, 'one-time-code');\n        setAuthToken(response.token);",
    "        AuthStorage.setItem(AUTH_PROVIDER_KEY, 'one-time-code');\n        AuthStorage.removeItem(ENTRA_LOGOUT_HINT_KEY);\n        setAuthToken(response.token);",
)
replace_once(
    auth_context,
    "        AuthStorage.setItem(AUTH_PROVIDER_KEY, 'development');\n        setAuthToken(response.token);",
    "        AuthStorage.setItem(AUTH_PROVIDER_KEY, 'development');\n        AuthStorage.removeItem(ENTRA_LOGOUT_HINT_KEY);\n        setAuthToken(response.token);",
)
replace_once(
    auth_context,
    "    AuthStorage.removeItem(AUTH_PROVIDER_KEY);\n    clearReauthInFlight();",
    "    AuthStorage.removeItem(AUTH_PROVIDER_KEY);\n    AuthStorage.removeItem(ENTRA_LOGOUT_HINT_KEY);\n    clearReauthInFlight();",
)
replace_once(
    auth_context,
    "    const authProvider = AuthStorage.getItem(AUTH_PROVIDER_KEY);\n    const shouldEndMicrosoftSession",
    "    const authProvider = AuthStorage.getItem(AUTH_PROVIDER_KEY);\n    const logoutHint = AuthStorage.getItem(ENTRA_LOGOUT_HINT_KEY);\n    const shouldEndMicrosoftSession",
)
replace_once(
    auth_context,
    "        startEntraLogout();",
    "        startEntraLogout(logoutHint);",
)

login_route = "src/FE/src/features/auth/routes/Login.tsx"
replace_once(
    login_route,
    "  AUTH_PROVIDER_KEY,\n  AUTH_TOKEN_KEY,",
    "  AUTH_PROVIDER_KEY,\n  AUTH_TOKEN_KEY,\n  ENTRA_LOGOUT_HINT_KEY,",
)
replace_once(
    login_route,
    "          AuthStorage.setItem(AUTH_PROVIDER_KEY, 'microsoft');\n          clearReauthInFlight();",
    "          AuthStorage.setItem(AUTH_PROVIDER_KEY, 'microsoft');\n          if (result.logoutHint) {\n            AuthStorage.setItem(ENTRA_LOGOUT_HINT_KEY, result.logoutHint);\n          } else {\n            AuthStorage.removeItem(ENTRA_LOGOUT_HINT_KEY);\n          }\n          clearReauthInFlight();",
)

invite_route = "src/FE/src/features/auth/routes/InviteAccept.tsx"
replace_once(
    invite_route,
    "import { AUTH_PROVIDER_KEY, AUTH_TOKEN_KEY, USER_EMAIL_KEY, AuthStorage } from '../../../providers/authContextValue';",
    "import {\n  AUTH_PROVIDER_KEY,\n  AUTH_TOKEN_KEY,\n  ENTRA_LOGOUT_HINT_KEY,\n  USER_EMAIL_KEY,\n  AuthStorage,\n} from '../../../providers/authContextValue';",
)
replace_once(
    invite_route,
    "        .then(response => {\n          AuthStorage.setItem(AUTH_TOKEN_KEY, response.token);\n          AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);\n          AuthStorage.setItem(AUTH_PROVIDER_KEY, 'microsoft');",
    "        .then(result => {\n          const response = result.auth;\n          AuthStorage.setItem(AUTH_TOKEN_KEY, response.token);\n          AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);\n          AuthStorage.setItem(AUTH_PROVIDER_KEY, 'microsoft');\n          if (result.logoutHint) {\n            AuthStorage.setItem(ENTRA_LOGOUT_HINT_KEY, result.logoutHint);\n          } else {\n            AuthStorage.removeItem(ENTRA_LOGOUT_HINT_KEY);\n          }",
)

deploy_entra = "src/BE/infrastructure/deploy-entra.ps1"
replace_once(
    deploy_entra,
    "    api = [ordered]@{\n        requestedAccessTokenVersion = 2\n    }\n    spa = [ordered]@{",
    "    api = [ordered]@{\n        requestedAccessTokenVersion = 2\n    }\n    # The browser stores this opaque ID-token claim and sends it as logout_hint\n    # so Microsoft can end the correct session without an account picker.\n    optionalClaims = [ordered]@{\n        idToken = @(\n            [ordered]@{\n                name = 'login_hint'\n                source = $null\n                essential = $false\n                additionalProperties = @()\n            }\n        )\n        accessToken = @()\n        saml2Token = @()\n    }\n    spa = [ordered]@{",
)

fe_readme = "src/FE/README.md"
replace_once(
    fe_readme,
    "Explicit user logout clears the Workslip JWT, saved email, authentication-provider marker, reauthentication state, PKCE state and authenticated query cache before navigation. Sessions authenticated with Microsoft then redirect through the tenant-specific Microsoft Entra `/oauth2/v2.0/logout` endpoint and return to the current origin's `/login` route through `post_logout_redirect_uri`.",
    "Explicit user logout clears the Workslip JWT, saved email, authentication-provider marker, reauthentication state, PKCE state and authenticated query cache before navigation. Microsoft sign-in always adds the OIDC `openid profile` scopes, reads the opaque `login_hint` claim from the returned ID token, and stores only that hint. Microsoft logout sends it as `logout_hint`, which identifies the session without showing Microsoft's logout account picker, then returns to the current origin's `/login` route through `post_logout_redirect_uri`.",
)
replace_once(
    fe_readme,
    "Microsoft logout ends the active browser SSO session but does not remove remembered account tiles from the operating system, Outlook, Authenticator or Microsoft's account chooser.",
    "The Entra client registration must expose the `login_hint` optional ID-token claim; `deploy-entra.ps1` reconciles it. Sessions created before that configuration is deployed and the user signs in again have no stored hint and can still see Microsoft's account picker once. Microsoft logout does not remove remembered account tiles from the operating system, Outlook, Authenticator or Microsoft's sign-in account chooser.",
)

infra_readme = "src/BE/infrastructure/README.md"
replace_once(
    infra_readme,
    "The script preserves existing managed role/scope IDs and does not create an OAuth client secret. The browser authenticates with authorization code + PKCE; the API validates bearer tokens.",
    "The script preserves existing managed role/scope IDs and does not create an OAuth client secret. The browser authenticates with authorization code + PKCE; the API validates bearer tokens. The client registration also requests the `login_hint` optional ID-token claim so explicit logout can identify the active Microsoft session and return directly to Workslip without a logout account picker.",
)

# Guard the key behavior after all replacements.
checks = {
    entra_login: ["logout_hint", "ensureOidcScopes", "extractEntraLogoutHint"],
    invite_api: ["ensureOidcScopes", "extractEntraLogoutHint"],
    auth_context: ["startEntraLogout(logoutHint)", "ENTRA_LOGOUT_HINT_KEY"],
    deploy_entra: ["name = 'login_hint'", "optionalClaims"],
}
for path, required_values in checks.items():
    content = Path(path).read_text(encoding="utf-8")
    for required_value in required_values:
        if required_value not in content:
            raise RuntimeError(f"Missing expected value {required_value!r} in {path}")
