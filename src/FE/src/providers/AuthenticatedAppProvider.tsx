import { useCallback, useEffect, useMemo } from 'react';
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
  AUTH_TRANSITION_ATTRIBUTE,
  type AuthContextType,
} from './authContextValue';
import { canUseSessionNotifications } from './sessionFeaturePolicy';

interface AuthenticatedSessionEffectsProps {
  login: AuthContextType['login'];
  establishSession: AuthContextType['establishSession'];
  clearSession: () => void;
  onValueChange: (value: AuthContextType) => void;
}

function shouldPrefetchJobs(): boolean {
  return window.location.pathname === '/'
    || window.location.pathname === '/login'
    || window.location.pathname === '/app'
    || window.location.pathname === '/app/';
}

/**
 * Runs the authenticated session side-effects (identity query, push
 * reconciliation, initial job prefetch) and reports the resolved auth value up
 * via `onValueChange`. It renders nothing and sits as a sibling of the routed
 * app so that logging in or out never unmounts and remounts the router. Only
 * mounted while a token exists; the react-query provider is supplied by the
 * always-mounted AuthProvider ancestor.
 */
export function AuthenticatedSessionEffects({
  login,
  establishSession,
  clearSession,
  onValueChange,
}: AuthenticatedSessionEffectsProps) {
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
    if (!isAuthenticated) return undefined;

    let frameId = 0;
    const releaseTransitionWhenShellIsReady = () => {
      if (!document.querySelector('.app-shell')) {
        frameId = window.requestAnimationFrame(releaseTransitionWhenShellIsReady);
        return;
      }

      frameId = window.requestAnimationFrame(() => {
        document.documentElement.removeAttribute(AUTH_TRANSITION_ATTRIBUTE);
      });
    };

    frameId = window.requestAnimationFrame(releaseTransitionWhenShellIsReady);
    return () => window.cancelAnimationFrame(frameId);
  }, [isAuthenticated]);

  useEffect(() => {
    if (!isAuthenticated || !canUseNotifications || !user?.id) return;

    let registrationInFlight = false;

    const reconcilePushSubscription = async () => {
      if (registrationInFlight) return;
      registrationInFlight = true;

      try {
        await registerPush();
      } catch (error) {
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
    void prefetchInitialJobList(queryClient).catch(() => undefined);
  }, [isAuthenticated, usesPrimaryJobList]);

  const clearLocalSession = useCallback(() => {
    queryClient.clear();
    clearSession();
  }, [clearSession]);

  const logout = useCallback(() => {
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
      establishSession,
      logout,
      clearLocalSession,
      updateUser,
      meQuery: publicMeQuery,
    }),
    [clearLocalSession, establishSession, isAuthenticated, login, logout, meQuery.isPending, publicMeQuery, updateUser, user],
  );

  useEffect(() => {
    onValueChange(value);
  }, [value, onValueChange]);

  return null;
}
