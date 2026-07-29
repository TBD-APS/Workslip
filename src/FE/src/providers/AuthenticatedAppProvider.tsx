import { useCallback, useEffect, useMemo, type ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { useGetApiAuthMe, getGetApiAuthMeQueryKey } from '../api/generated/auth/auth';
import type { UserViewModel } from '../api/generated/models';
import { prefetchInitialJobList } from '../features/jobs/queries/jobListQuery';
import { usePushNotifications } from '../features/users/hooks/usePushNotifications';
import { queryClient } from '../lib/react-query';
import {
  AuthContext,
  type AuthContextType,
} from './authContextValue';

interface AuthenticatedAppProviderProps {
  children: ReactNode;
  login: AuthContextType['login'];
  devLogin: AuthContextType['devLogin'];
  clearSession: () => void;
}

function AuthenticatedSessionProvider({
  children,
  login,
  devLogin,
  clearSession,
}: AuthenticatedAppProviderProps) {
  const { register: registerPush } = usePushNotifications();

  const meQuery = useGetApiAuthMe({
    query: {
      enabled: true,
      retry: 1,
      retryDelay: 500,
      refetchOnReconnect: true,
      staleTime: 5 * 60 * 1000,
    },
  });

  const user = meQuery.data ?? null;
  const isAuthenticated = Boolean(user);

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

  useEffect(() => {
    if (!isAuthenticated) return;

    // The token and current user are now validated. Populate the same query key
    // used by JobList so the home route can render cached data immediately.
    void prefetchInitialJobList(queryClient).catch(() => undefined);
  }, [isAuthenticated]);

  const logout = useCallback(() => {
    queryClient.clear();
    clearSession();
  }, [clearSession]);

  const updateUser = useCallback(
    (partial: Partial<Pick<UserViewModel, 'displayName' | 'phone'>>) => {
      queryClient.setQueryData(getGetApiAuthMeQueryKey(), (old: UserViewModel | undefined) => {
        if (!old) return old;
        return { ...old, ...partial };
      });
    },
    [],
  );

  const retryMe = useCallback(async (): Promise<unknown> => {
    await queryClient.cancelQueries({ queryKey: getGetApiAuthMeQueryKey() });
    return meQuery.refetch();
  }, [meQuery.refetch]);

  const publicMeQuery = useMemo(
    () => ({
      isPending: meQuery.isPending,
      isError: meQuery.isError,
      refetch: retryMe,
      data: meQuery.data ?? null,
    }),
    [meQuery.isPending, meQuery.isError, meQuery.data, retryMe],
  );

  const value = useMemo<AuthContextType>(
    () => ({
      hasAuthToken: true,
      isAuthenticated,
      user,
      isLoading: meQuery.isPending,
      login,
      devLogin,
      logout,
      updateUser,
      meQuery: publicMeQuery,
    }),
    [devLogin, isAuthenticated, login, logout, meQuery.isPending, publicMeQuery, updateUser, user],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function AuthenticatedAppProvider(props: AuthenticatedAppProviderProps) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthenticatedSessionProvider {...props} />
    </QueryClientProvider>
  );
}
