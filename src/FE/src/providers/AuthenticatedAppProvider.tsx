import { useCallback, useEffect, useMemo, type ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { useGetApiAuthMe, getGetApiAuthMeQueryKey } from '../api/generated/auth/auth';
import type { UserViewModel } from '../api/generated/models';
import {
  DEFAULT_AUTHENTICATED_PATH,
  getAuthenticatedHomePath,
} from '../features/auth/authenticatedDestination';
import { prefetchInitialJobList } from '../features/jobs/queries/jobListQuery';
import { usePushNotifications } from '../features/users/hooks/usePushNotifications';
import { queryClient } from '../lib/react-query';
import {
  AuthContext,
  type AuthContextType,
} from './authContextValue';
import { canUseSessionNotifications } from './sessionFeaturePolicy';

interface AuthenticatedAppProviderProps {
  children: ReactNode;
  login: AuthContextType['login'];
  devLogin: AuthContextType['devLogin'];
  clearSession: () => void;
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
  const canUseNotifications = canUseSessionNotifications(user?.role);
  const usesPrimaryJobList = getAuthenticatedHomePath(user?.role) === DEFAULT_AUTHENTICATED_PATH;

  useEffect(() => {
    if (!isAuthenticated || !canUseNotifications || !user?.id) return;

    let registrationInFlight = false;

    const reconcilePushSubscription = async () => {
      if (registrationInFlight) return;
      registrationInFlight = true;

      try {
        await registerPush();
      } catch (error) {
        // Registration is best-effort here. Returning to the foreground or
        // reconnecting gives the session another chance to repair a missing
        // server-side subscription without exposing endpoint/key material.
        console.error('[Auth] Failed to reconcile push notifications:', error);
      } finally {
        registrationInFlight = false;
      }
    };

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        void reconcilePushSubscription();
      }
    };

    const handleOnline = () => {
      void reconcilePushSubscription();
    };

    void reconcilePushSubscription();
    document.addEventListener('visibilitychange', handleVisibilityChange);
    window.addEventListener('online', handleOnline);

    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange);
      window.removeEventListener('online', handleOnline);
    };
  }, [canUseNotifications, isAuthenticated, registerPush, user?.id]);

  useEffect(() => {
    if (!isAuthenticated || !usesPrimaryJobList || !shouldPrefetchJobs()) return;

    // The token and current user are now validated. Populate the same query key
    // used by JobList so the home route can render cached data immediately.
    void prefetchInitialJobList(queryClient).catch(() => undefined);
  }, [isAuthenticated, usesPrimaryJobList]);

  const clearLocalSession = useCallback(() => {
    queryClient.clear();
    clearSession();
  }, [clearSession]);

  const logout = useCallback(() => {
    // Keep explicit logout separate from internal session clearing so a future
    // provider-specific sign-out cannot affect invitation or recovery flows.
    clearLocalSession();
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
