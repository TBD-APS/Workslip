import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { verifyAuthCode, getDevToken } from '../features/auth/api/devToken';
import { useGetApiAuthMe, getGetApiAuthMeQueryKey } from '../api/generated/auth/auth';
import type { UserViewModel } from '../api/generated/models';
import { AUTH_TOKEN_KEY, AuthContext, USER_EMAIL_KEY, AuthStorage, clearReauthInFlight } from './authContextValue';
import { usePushNotifications } from '../features/users/hooks/usePushNotifications';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [authToken, setAuthToken] = useState<string | null>(() => AuthStorage.getItem(AUTH_TOKEN_KEY));
  const queryClient = useQueryClient();
  const { register: registerPush } = usePushNotifications();

  const meQuery = useGetApiAuthMe({
    query: {
      enabled: Boolean(authToken),
      retry: 1,
      staleTime: 5 * 60 * 1000,
    },
  });

  const user = meQuery.data ?? null;
  const isAuthenticated = Boolean(authToken) && Boolean(user);
  const isLoading = Boolean(authToken) && meQuery.isPending;

  // Register push notifications when the user becomes authenticated
  useEffect(() => {
    if (isAuthenticated) {
      registerPush().catch((err) => {
        console.error('[Auth] Failed to register push notifications:', err);
      });
    }
  }, [isAuthenticated]);

  const login = useCallback(
    async (email: string, code: string): Promise<boolean> => {
      try {
        const response = await verifyAuthCode(email, code);
        AuthStorage.setItem(AUTH_TOKEN_KEY, response.token);
        AuthStorage.setItem(USER_EMAIL_KEY, response.user.email);
        setAuthToken(response.token);
        // Successful login clears any in-flight reauth redirect so the next expiry can re-trigger.
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
        setAuthToken(response.token);
        clearReauthInFlight();
        return true;
      } catch {
        return false;
      }
    },
    [],
  );

  const logout = useCallback(() => {
    AuthStorage.removeItem(AUTH_TOKEN_KEY);
    AuthStorage.removeItem(USER_EMAIL_KEY);
    clearReauthInFlight();
    setAuthToken(null);
    queryClient.clear();
  }, [queryClient]);

  const updateUser = useCallback(
    (partial: Partial<Pick<UserViewModel, 'displayName' | 'phone'>>) => {
      queryClient.setQueryData(getGetApiAuthMeQueryKey(), (old: UserViewModel | undefined) => {
        if (!old) return old;
        return { ...old, ...partial };
      });
    },
    [queryClient],
  );

  // Expose a stable, narrow shape of meQuery so ProtectedRoute can do one short
  // retry before declaring the user signed out (handles transient failures
  // after PWA service-worker swaps).
  const publicMeQuery = useMemo(
    () => ({
      isPending: meQuery.isPending,
      isError: meQuery.isError,
      refetch: meQuery.refetch,
      data: meQuery.data ?? null,
    }),
    [meQuery.isPending, meQuery.isError, meQuery.refetch, meQuery.data],
  );

  return (
    <AuthContext.Provider
      value={{
        isAuthenticated,
        user,
        isLoading,
        login,
        devLogin,
        logout,
        updateUser,
        meQuery: publicMeQuery,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}
