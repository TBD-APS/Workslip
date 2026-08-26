import { lazy, Suspense, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../lib/react-query';
import { preloadPrimaryAppRoute } from '../routes/preloadPrimaryAppRoute';
import {
  AUTH_TOKEN_KEY,
  AUTH_TRANSITION_ATTRIBUTE,
  AuthContext,
  AuthStorage,
  clearReauthInFlight,
  type AuthContextType,
  type AuthMeQuery,
  USER_EMAIL_KEY,
} from './authContextValue';

// The authenticated session logic (identity query, push reconciliation, job
// prefetch) stays code-split. It no longer wraps the app; it runs as a sibling
// of `children` and reports the resolved auth value up, so the routed app is
// never unmounted and remounted when the token flips at login/logout.
const AuthenticatedSessionEffects = lazy(() =>
  import('./AuthenticatedAppProvider').then((module) => ({
    default: module.AuthenticatedSessionEffects,
  })),
);

const publicMeQuery: AuthMeQuery = {
  isPending: false,
  isError: false,
  refetch: async () => null,
  data: null,
};

const pendingMeQuery: AuthMeQuery = {
  isPending: true,
  isError: false,
  refetch: async () => null,
  data: null,
};

export function AuthProvider({ children }: { children: ReactNode }) {
  const [authToken, setAuthToken] = useState<string | null>(() => AuthStorage.getItem(AUTH_TOKEN_KEY));
  // Populated by AuthenticatedSessionEffects once the identity query resolves.
  const [authedValue, setAuthedValue] = useState<AuthContextType | null>(null);

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
    setAuthedValue(null);
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

  // A token exists but the authenticated session module has not reported real
  // identity yet. Expose a loading identity so route guards keep waiting (and
  // never flash the login card) exactly as before.
  const pendingValue = useMemo<AuthContextType>(
    () => ({
      ...publicValue,
      hasAuthToken: true,
      isLoading: true,
      meQuery: pendingMeQuery,
    }),
    [publicValue],
  );

  const value = authToken ? authedValue ?? pendingValue : publicValue;

  return (
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={value}>
        {authToken && (
          <Suspense fallback={null}>
            <AuthenticatedSessionEffects
              login={login}
              establishSession={establishSession}
              clearSession={clearStoredSession}
              onValueChange={setAuthedValue}
            />
          </Suspense>
        )}
        {children}
      </AuthContext.Provider>
    </QueryClientProvider>
  );
}
