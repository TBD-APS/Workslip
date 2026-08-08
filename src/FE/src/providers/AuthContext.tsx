import { lazy, Suspense, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { FullscreenSystemState } from '../components/common/FullscreenSystemState';
import { preloadPrimaryAppRoute } from '../routes/preloadPrimaryAppRoute';
import {
  AUTH_TOKEN_KEY,
  AuthContext,
  AuthStorage,
  clearReauthInFlight,
  type AuthContextType,
  USER_EMAIL_KEY,
} from './authContextValue';

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

  const publicValue = useMemo<AuthContextType>(
    () => ({
      hasAuthToken: false,
      isAuthenticated: false,
      user: null,
      isLoading: false,
      login,
      logout: clearStoredSession,
      clearLocalSession: clearStoredSession,
      updateUser: () => undefined,
      meQuery: publicMeQuery,
    }),
    [clearStoredSession, login],
  );

  if (!authToken) {
    return <AuthContext.Provider value={publicValue}>{children}</AuthContext.Provider>;
  }

  return (
    <Suspense
      fallback={(
        <FullscreenSystemState
          title="Tjekker login"
          message="Vi kontrollerer din session og gør Workslip klar."
        />
      )}
    >
      <AuthenticatedAppProvider
        login={login}
        clearSession={clearStoredSession}
      >
        {children}
      </AuthenticatedAppProvider>
    </Suspense>
  );
}
