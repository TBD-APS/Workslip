import { lazy, Suspense, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  AUTH_TOKEN_KEY,
  AuthContext,
  USER_EMAIL_KEY,
  AuthStorage,
  clearReauthInFlight,
  type AuthContextType,
} from './authContextValue';
import { preloadPrimaryAppRoute } from '../routes/preloadPrimaryAppRoute';

const AuthenticatedAppProvider = lazy(() =>
  import('./AuthenticatedAppProvider').then((module) => ({ default: module.AuthenticatedAppProvider })),
);

const publicMeQuery: AuthContextType['meQuery'] = {
  isPending: false,
  isError: false,
  refetch: async () => null,
  data: null,
};

export function AuthProvider({ children }: { children: ReactNode }) {
  const [authToken, setAuthToken] = useState<string | null>(() => AuthStorage.getItem(AUTH_TOKEN_KEY));

  useEffect(() => {
    if (!authToken) return;

    // Start the shell and jobs-route downloads alongside the authenticated
    // provider and /api/auth/me request. Import failures still flow through the
    // existing Vite stale-chunk recovery when the route is rendered.
    void preloadPrimaryAppRoute().catch(() => undefined);
  }, [authToken]);

  const clearStoredSession = useCallback(() => {
    AuthStorage.removeItem(AUTH_TOKEN_KEY);
    AuthStorage.removeItem(USER_EMAIL_KEY);
    clearReauthInFlight();
    setAuthToken(null);
  }, []);

  const login = useCallback(
    async (email: string, code: string): Promise<boolean> => {
      try {
        const { verifyAuthCode } = await import('../features/auth/api/devToken');
        const response = await verifyAuthCode(email, code);
        AuthStorage.setItem(AUTH_TOKEN_KEY, response.token);
        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);
        setAuthToken(response.token);
        clearReauthInFlight();
        return true;
      } catch {
        return false;
      }
    },
    [],
  );

  const devLogin = useCallback(
    async (email: string): Promise<boolean> => {
      try {
        const { getDevToken } = await import('../features/auth/api/devToken');
        const response = await getDevToken(email);
        AuthStorage.setItem(AUTH_TOKEN_KEY, response.token);
        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);
        setAuthToken(response.token);
        clearReauthInFlight();
        return true;
      } catch {
        return false;
      }
    },
    [],
  );

  const publicValue = useMemo<AuthContextType>(
    () => ({
      hasAuthToken: false,
      isAuthenticated: false,
      user: null,
      isLoading: false,
      login,
      devLogin,
      logout: clearStoredSession,
      clearLocalSession: clearStoredSession,
      updateUser: () => undefined,
      meQuery: publicMeQuery,
    }),
    [clearStoredSession, devLogin, login],
  );

  if (!authToken) {
    return <AuthContext.Provider value={publicValue}>{children}</AuthContext.Provider>;
  }

  return (
    <Suspense
      fallback={(
        <div className="protected-route-loading" role="status" aria-live="polite">
          Tjekker login status...
        </div>
      )}
    >
      <AuthenticatedAppProvider
        login={login}
        devLogin={devLogin}
        clearSession={clearStoredSession}
      >
        {children}
      </AuthenticatedAppProvider>
    </Suspense>
  );
}
