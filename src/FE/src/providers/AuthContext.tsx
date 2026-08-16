import { lazy, Suspense, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { FullscreenSystemState } from '../components/common/FullscreenSystemState';
import { preloadPrimaryAppRoute } from '../routes/preloadPrimaryAppRoute';
import {
  AUTH_TOKEN_KEY,
  AUTH_TRANSITION_ATTRIBUTE,
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

  const establishSession = useCallback<AuthContextType['establishSession']>((token, email, role) => {
    document.documentElement.setAttribute(AUTH_TRANSITION_ATTRIBUTE, '');
    void preloadPrimaryAppRoute(role).catch(() => undefined);
    AuthStorage.setItem(AUTH_TOKEN_KEY, token);
    AuthStorage.setItem(USER_EMAIL_KEY, email);
    clearReauthInFlight();
    setAuthToken(token);
  }, []);

  const clearStoredSession = useCallback(() => {
    AuthStorage.removeItem(AUTH_TOKEN_KEY);
    AuthStorage.removeItem(USER_EMAIL_KEY);
    clearReauthInFlight();
    document.documentElement.removeAttribute(AUTH_TRANSITION_ATTRIBUTE);
    setAuthToken(null);
  }, []);

  const rejectStoredSession = useCallback(() => {
    AuthStorage.removeItem(AUTH_TOKEN_KEY);
    clearReauthInFlight();
    document.documentElement.removeAttribute(AUTH_TRANSITION_ATTRIBUTE);
    setAuthToken(null);
  }, []);

  const login = useCallback(
    async (email: string, code: string): Promise<string | null> => {
      try {
        const { verifyAuthCode } = await import('../features/auth/api/devToken');
        const response = await verifyAuthCode(email, code);
        establishSession(response.token, response.user.email, response.user.role);
        return response.user.role;
      } catch {
        return null;
      }
    },
    [establishSession],
  );

  const publicValue = useMemo<AuthContextType>(
    () => ({
      hasAuthToken: false,
      isAuthenticated: false,
      user: null,
      isLoading: false,
      login,
      establishSession,
      logout: clearStoredSession,
      clearLocalSession: clearStoredSession,
      updateUser: () => undefined,
      meQuery: publicMeQuery,
    }),
    [clearStoredSession, establishSession, login],
  );

  if (!authToken) {
    return <AuthContext.Provider value={publicValue}>{children}</AuthContext.Provider>;
  }

  return (
    <Suspense
      fallback={(
        <FullscreenSystemState
          title="Tjekker login"
          message="Vi kontrollerer din session og forbinder til Workslip."
        />
      )}
    >
      <AuthenticatedAppProvider
        login={login}
        establishSession={establishSession}
        clearSession={clearStoredSession}
        rejectSession={rejectStoredSession}
      >
        {children}
      </AuthenticatedAppProvider>
    </Suspense>
  );
}
