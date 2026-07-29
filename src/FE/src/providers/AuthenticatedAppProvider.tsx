import { useCallback, useEffect, useMemo, type ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { useGetApiAuthMe, getGetApiAuthMeQueryKey } from '../api/generated/auth/auth';
import type { UserViewModel } from '../api/generated/models';
import { prefetchInitialJobList } from '../features/jobs/queries/jobListQuery';
import { usePushNotifications } from '../features/users/hooks/usePushNotifications';
import { queryClient } from '../lib/react-query';
import {
  AUTH_PROVIDER_KEY,
  ENTRA_LOGOUT_HINT_KEY,
  AuthContext,
  AuthStorage,
  type AuthContextType,
} from './authContextValue';

interface AuthenticatedAppProviderProps {
  children: ReactNode;
  login: AuthContextType['login'];
  devLogin: AuthContextType['devLogin'];
  clearStoredSession: () => void;
}

function shouldPrefetchJobs(): boolean {
  return window.location.pathname === '/'
    || window.location.pathname === '/login'
    || window.location.pathname === '/app'
    || window.location.pathname === '/app/';
}

function AuthenticatedSessionProvider({
  children,
  login,
  devLogin,
  clearStoredSession,
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
    if (!isAuthenticated || !shouldPrefetchJobs()) return;

    // The token and current user are now validated. Populate the same query key
    // used by JobList so the home route can render cached data immediately.
    void prefetchInitialJobList(queryClient).catch(() => undefined);
  }, [isAuthenticated]);

  const clearLocalSession = useCallback(() => {
    queryClient.clear();
    clearStoredSession();
  }, [clearStoredSession]);

  const logout = useCallback(() => {
    const authProvider = AuthStorage.getItem(AUTH_PROVIDER_KEY);
    const logoutHint = AuthStorage.getItem(ENTRA_LOGOUT_HINT_KEY);
    const shouldEndMicrosoftSession = authProvider === null || authProvider === 'microsoft';

    clearLocalSession();

    if (!shouldEndMicrosoftSession) {
      window.location.replace('/login');
      return;
    }

    void import('../features/auth/api/entraLogin')
      .then(({ startEntraLogout }) => startEntraLogout(logoutHint))
      .catch((error: unknown) => {
        console.error('[Auth] Failed to start Microsoft logout.', error);
        window.location.replace('/login');
      });
  }, [clearLocalSession]);

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
      clearLocalSession,
      updateUser,
      meQuery: publicMeQuery,
    }),
    [clearLocalSession, devLogin, isAuthenticated, login, logout, meQuery.isPending, publicMeQuery, updateUser, user],
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
