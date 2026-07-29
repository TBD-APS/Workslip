from pathlib import Path


def replace_exact(path: str, old: str, new: str) -> None:
    file_path = Path(path)
    content = file_path.read_text(encoding="utf-8-sig")
    if old not in content:
        raise RuntimeError(f"Expected text not found in {path}:\n{old}")
    file_path.write_text(content.replace(old, new, 1), encoding="utf-8", newline="\n")


auth_value = "src/FE/src/providers/authContextValue.ts"
entra_login = "src/FE/src/features/auth/api/entraLogin.ts"
auth_context = "src/FE/src/providers/AuthContext.tsx"
login_route = "src/FE/src/features/auth/routes/Login.tsx"
invite_route = "src/FE/src/features/auth/routes/InviteAccept.tsx"
app_layout = "src/FE/src/components/layouts/AppLayout.tsx"
error_fallback = "src/FE/src/providers/ErrorFallback.tsx"
error_state = "src/FE/src/components/ErrorState.tsx"
routes = "src/FE/src/routes/index.tsx"
readme = "src/FE/README.md"

replace_exact(
    auth_value,
    "export const USER_EMAIL_KEY = 'userEmail';\nexport const REAUTH_IN_FLIGHT_KEY = 'workslip.reauthInFlight';",
    "export const USER_EMAIL_KEY = 'userEmail';\nexport const AUTH_PROVIDER_KEY = 'authProvider';\nexport const REAUTH_IN_FLIGHT_KEY = 'workslip.reauthInFlight';\n\nexport type AuthProvider = 'microsoft' | 'one-time-code' | 'development';",
)
replace_exact(
    auth_value,
    "  logout: () => void;\n  updateUser:",
    "  logout: () => void;\n  clearLocalSession: () => void;\n  updateUser:",
)

replace_exact(
    entra_login,
    "export const clearEntraLoginSession = () => {\n  sessionStorage.removeItem(PKCE_KEY);\n};\n\nexport const sanitizeReturnTo",
    "export const clearEntraLoginSession = () => {\n  sessionStorage.removeItem(PKCE_KEY);\n};\n\nexport const buildEntraLogoutUrl = (tenantId: string, postLogoutRedirectUri: string): string => {\n  const logoutUrl = new URL(\n    `https://login.microsoftonline.com/${encodeURIComponent(tenantId)}/oauth2/v2.0/logout`,\n  );\n  logoutUrl.searchParams.set('post_logout_redirect_uri', postLogoutRedirectUri);\n  return logoutUrl.toString();\n};\n\nexport const startEntraLogout = () => {\n  const tenantId = getEntraTenantId();\n  clearEntraLoginSession();\n  window.location.replace(\n    buildEntraLogoutUrl(tenantId, `${window.location.origin}/login`),\n  );\n};\n\nexport const sanitizeReturnTo",
)
replace_exact(
    entra_login,
    "const getOAuthConfig = () => {\n  const tenantId = import.meta.env.VITE_AZURE_AD_TENANT_ID;",
    "const getEntraTenantId = () => {\n  const tenantId = import.meta.env.VITE_AZURE_AD_TENANT_ID;\n  if (!tenantId) {\n    throw new Error('Microsoft login mangler VITE_AZURE_AD_TENANT_ID.');\n  }\n  return tenantId;\n};\n\nconst getOAuthConfig = () => {\n  const tenantId = getEntraTenantId();",
)
replace_exact(
    entra_login,
    "  if (!tenantId || !clientId || !scope) {",
    "  if (!clientId || !scope) {",
)

replace_exact(
    auth_context,
    "import { verifyAuthCode, getDevToken } from '../features/auth/api/devToken';",
    "import { verifyAuthCode, getDevToken } from '../features/auth/api/devToken';\nimport { clearEntraLoginSession, startEntraLogout } from '../features/auth/api/entraLogin';",
)
replace_exact(
    auth_context,
    "import { AUTH_TOKEN_KEY, AuthContext, USER_EMAIL_KEY, AuthStorage, clearReauthInFlight } from './authContextValue';",
    "import {\n  AUTH_PROVIDER_KEY,\n  AUTH_TOKEN_KEY,\n  AuthContext,\n  USER_EMAIL_KEY,\n  AuthStorage,\n  clearReauthInFlight,\n} from './authContextValue';",
)
replace_exact(
    auth_context,
    "        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);\n        setAuthToken(response.token);",
    "        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);\n        AuthStorage.setItem(AUTH_PROVIDER_KEY, 'one-time-code');\n        setAuthToken(response.token);",
)
replace_exact(
    auth_context,
    "        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);\n        setAuthToken(response.token);\n        clearReauthInFlight();\n        return true;\n      } catch {\n        return false;\n      }\n    },\n    [],\n  );\n\n  const logout",
    "        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);\n        AuthStorage.setItem(AUTH_PROVIDER_KEY, 'development');\n        setAuthToken(response.token);\n        clearReauthInFlight();\n        return true;\n      } catch {\n        return false;\n      }\n    },\n    [],\n  );\n\n  const clearLocalSession = useCallback(() => {\n    AuthStorage.removeItem(AUTH_TOKEN_KEY);\n    AuthStorage.removeItem(USER_EMAIL_KEY);\n    AuthStorage.removeItem(AUTH_PROVIDER_KEY);\n    clearReauthInFlight();\n    clearEntraLoginSession();\n    setAuthToken(null);\n    queryClient.clear();\n  }, [queryClient]);\n\n  const logout",
)
replace_exact(
    auth_context,
    "  const logout = useCallback(() => {\n    AuthStorage.removeItem(AUTH_TOKEN_KEY);\n    AuthStorage.removeItem(USER_EMAIL_KEY);\n    clearReauthInFlight();\n    setAuthToken(null);\n    queryClient.clear();\n  }, [queryClient]);",
    "  const logout = useCallback(() => {\n    const authProvider = AuthStorage.getItem(AUTH_PROVIDER_KEY);\n    const shouldEndMicrosoftSession = authProvider === null || authProvider === 'microsoft';\n\n    clearLocalSession();\n\n    if (shouldEndMicrosoftSession) {\n      try {\n        startEntraLogout();\n        return;\n      } catch (error) {\n        console.error('[Auth] Failed to start Microsoft logout.', error);\n      }\n    }\n\n    window.location.replace('/login');\n  }, [clearLocalSession]);",
)
replace_exact(
    auth_context,
    "        logout,\n        updateUser,",
    "        logout,\n        clearLocalSession,\n        updateUser,",
)

replace_exact(
    login_route,
    "  AUTH_TOKEN_KEY,\n  USER_EMAIL_KEY,",
    "  AUTH_PROVIDER_KEY,\n  AUTH_TOKEN_KEY,\n  USER_EMAIL_KEY,",
)
replace_exact(
    login_route,
    "          AuthStorage.setItem(USER_EMAIL_KEY, result.auth.user.email);\n          clearReauthInFlight();",
    "          AuthStorage.setItem(USER_EMAIL_KEY, result.auth.user.email);\n          AuthStorage.setItem(AUTH_PROVIDER_KEY, 'microsoft');\n          clearReauthInFlight();",
)

replace_exact(
    invite_route,
    "import { AUTH_TOKEN_KEY, USER_EMAIL_KEY, AuthStorage } from '../../../providers/authContextValue';",
    "import { AUTH_PROVIDER_KEY, AUTH_TOKEN_KEY, USER_EMAIL_KEY, AuthStorage } from '../../../providers/authContextValue';",
)
replace_exact(
    invite_route,
    "  const { meQuery, logout, isLoading } = useAuth();",
    "  const { meQuery, clearLocalSession, isLoading } = useAuth();",
)
replace_exact(
    invite_route,
    "          AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);\n          clearInviteEnrollmentSession();",
    "          AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);\n          AuthStorage.setItem(AUTH_PROVIDER_KEY, 'microsoft');\n          clearInviteEnrollmentSession();",
)
replace_exact(invite_route, "          logout();", "          clearLocalSession();")
replace_exact(
    invite_route,
    "  }, [token, isLoading, meQuery.data, logout]);",
    "  }, [token, isLoading, meQuery.data, clearLocalSession]);",
)

replace_exact(
    app_layout,
    "  const handleLogout = () => {\n    logout();\n    // Navigate immediately rather than waiting for ProtectedRoute to render\n    // a <Navigate to=\"/login\"> — avoids a single frame of protected content\n    // still being visible after the user clicked logout, and prevents a\n    // browser-back race where the protected URL is briefly visible again.\n    navigate('/login', { replace: true });\n  };",
    "  const handleLogout = () => {\n    logout();\n  };",
)

replace_exact(
    error_fallback,
    "  const handleLogout = () => {\n    logout();\n    navigate('/login', { replace: true });\n  };",
    "  const handleLogout = () => {\n    logout();\n  };",
)

replace_exact(error_state, "import { useNavigate } from 'react-router-dom';\n", "")
replace_exact(error_state, "  const navigate = useNavigate();\n", "")
replace_exact(
    error_state,
    "  const handleLogout = () => {\n    logout();\n    navigate('/login', { replace: true });\n  };",
    "  const handleLogout = () => {\n    logout();\n  };",
)

replace_exact(
    routes,
    "  const { hasAuthToken, isAuthenticated, isLoading, logout, meQuery } = useAuth();",
    "  const { hasAuthToken, isAuthenticated, isLoading, clearLocalSession, meQuery } = useAuth();",
)
replace_exact(
    routes,
    "  const handleLogin = () => {\n    logout();\n    navigate(loginUrl, { replace: true });\n  };",
    "  const handleLogin = () => {\n    clearLocalSession();\n    navigate(loginUrl, { replace: true });\n  };",
)

replace_exact(
    readme,
    "The login route clears the PKCE state after success, cancellation or callback failure. Never persist the verifier in `localStorage`, logs, telemetry or URL parameters.\n\n## PWA caution",
    "The login route clears the PKCE state after success, cancellation or callback failure. Never persist the verifier in `localStorage`, logs, telemetry or URL parameters.\n\n## Microsoft logout\n\nExplicit user logout clears the Workslip JWT, saved email, authentication-provider marker, reauthentication state, PKCE state and authenticated query cache before navigation. Sessions authenticated with Microsoft then redirect through the tenant-specific Microsoft Entra `/oauth2/v2.0/logout` endpoint and return to the current origin's `/login` route through `post_logout_redirect_uri`.\n\nOne-time-code and development sessions clear only Workslip state. Internal account switching and startup recovery use `clearLocalSession` so they can discard an invalid or wrong Workslip identity without unexpectedly terminating the browser's Microsoft session. Existing sessions created before the authentication-provider marker was introduced are treated as Microsoft sessions on explicit logout.\n\nMicrosoft logout ends the active browser SSO session but does not remove remembered account tiles from the operating system, Outlook, Authenticator or Microsoft's account chooser.\n\n## PWA caution",
)

# Guard the intended split between explicit logout and local-only recovery.
for path in (app_layout, error_fallback, error_state):
    content = Path(path).read_text(encoding="utf-8")
    if "logout();" not in content:
        raise RuntimeError(f"Explicit logout call missing from {path}")

for path in (invite_route, routes):
    content = Path(path).read_text(encoding="utf-8")
    if "clearLocalSession" not in content:
        raise RuntimeError(f"Local-only session reset missing from {path}")
