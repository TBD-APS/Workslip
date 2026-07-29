import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { verifyAuthCode, getDevToken } from '../features/auth/api/devToken';
import { clearEntraLoginSession, startEntraLogout } from '../features/auth/api/entraLogin';
import { useGetApiAuthMe, getGetApiAuthMeQueryKey } from '../api/generated/auth/auth';
import type { UserViewModel } from '../api/generated/models';
import {
  AUTH_PROVIDER_KEY,
  AUTH_TOKEN_KEY,
  ENTRA_LOGOUT_HINT_KEY,
  AuthContext,
  USER_EMAIL_KEY,
  AuthStorage,
  clearReauthInFlight,
} from './authContextValue';
import { usePushNotifications } from '../features/users/hooks/usePushNotifications';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [authToken, setAuthToken] = useState<string | null>(() => AuthStorage.getItem(AUTH_TOKEN_KEY));
  const queryClient = useQueryClient();
  const { register: registerPush } = usePushNotifications();
  const hasAuthToken = Boolean(authToken);

  const meQuery = useGetApiAuthMe({
    query: {
      enabled: hasAuthToken,
      retry: 1,
      retryDelay: 500,
      refetchOnReconnect: true,
      staleTime: 5 * 60 * 1000,
    },
  });

  const user = meQuery.data ?? null;
  const isAuthenticated = hasAuthToken && Boolean(user);
  const isLoading = hasAuthToken && meQuery.isPending;

  useEffect(() => {
    if (isAuthenticated) {
      registerPush().catch((error) => {
        console.error('[Auth] Failed to register push notifications:', error);
      });
    }
    // The push hook currently returns a function tied to its mutation object.
    // Including it would re-run registration on every provider render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated]);

  const login = useCallback(
    async (email: string, code: string): Promise<boolean> => {
      try {
        const response = await verifyAuthCode(email, code);
        AuthStorage.setItem(AUTH_TOKEN_KEY, response.token);
        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);
        AuthStorage.setItem(AUTH_PROVIDER_KEY, 'one-time-code');
        AuthStorage.removeItem(ENTRA_LOGOUT_HINT_KEY);
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
        const response = await getDevToken(email);
        AuthStorage.setItem(AUTH_TOKEN_KEY, response.token);
        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);
        AuthStorage.setItem(AUTH_PROVIDER_KEY, 'development');
        AuthStorage.removeItem(ENTRA_LOGOUT_HINT_KEY);
        setAuthToken(response.token);
        clearReauthInFlight();
        return true;
      } catch {
        return false;
      }
    },
    [],
  );

  const clearLocalSession = useCallback(() => {
    AuthStorage.removeItem(AUTH_TOKEN_KEY);
    AuthStorage.removeItem(USER_EMAIL_KEY);
    AuthStorage.removeItem(AUTH_PROVIDER_KEY);
    AuthStorage.removeItem(ENTRA_LOGOUT_HINT_KEY);
    clearReauthInFlight();
    clearEntraLoginSession();
    setAuthToken(null);
    queryClient.clear();
  }, [queryClient]);

  const logout = useCallback(() => {
    const authProvider = AuthStorage.getItem(AUTH_PROVIDER_KEY);
    const logoutHint = AuthStorage.getItem(ENTRA_LOGOUT_HINT_KEY);
    const shouldEndMicrosoftSession = authProvider === null || authProvider === 'microsoft';

    clearLocalSession();

    if (shouldEndMicrosoftSession) {
      try {
        startEntraLogout(logoutHint);
        return;
      } catch (error) {
        console.error('[Auth] Failed to start Microsoft logout.', error);
      }
    }

    window.location.replace('/login');
  }, [clearLocalSession]);

  const updateUser = useCallback(
    (partial: Partial<Pick<UserViewModel, 'displayName' | 'phone'>>) => {
      queryClient.setQueryData(getGetApiAuthMeQueryKey(), (old: UserViewModel | undefined) => {
        if (!old) return old;
        return { ...old, ...partial };
      });
    },
    [queryClient],
  );

  const retryMe = useCallback(async (): Promise<unknown> => {
    await queryClient.cancelQueries({ queryKey: getGetApiAuthMeQueryKey() });
    return meQuery.refetch();
  }, [meQuery.refetch, queryClient]);

  const publicMeQuery = useMemo(
    () => ({
      isPending: meQuery.isPending,
      isError: meQuery.isError,
      refetch: retryMe,
      data: meQuery.data ?? null,
    }),
    [meQuery.isPending, meQuery.isError, meQuery.data, retryMe],
  );

  return (
    <AuthContext.Provider
      value={{
        hasAuthToken,
        isAuthenticated,
        user,
        isLoading,
        login,
        devLogin,
        logout,
        clearLocalSession,
        updateUser,
        meQuery: publicMeQuery,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}