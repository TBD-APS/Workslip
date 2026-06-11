import { useCallback, useState } from 'react';
import type { ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { verifyAuthCode, getDevToken } from '../features/auth/api/devToken';
import { useGetApiAuthMe } from '../api/generated/auth/auth';
import { AUTH_TOKEN_KEY, AuthContext, USER_EMAIL_KEY } from './authContextValue';


export function AuthProvider({ children }: { children: ReactNode }) {
  const [authToken, setAuthToken] = useState<string | null>(() => sessionStorage.getItem(AUTH_TOKEN_KEY));
  const queryClient = useQueryClient();

  const meQuery = useGetApiAuthMe({
    query: {
      enabled: Boolean(authToken),
      retry: false,
      staleTime: 5 * 60 * 1000,
    },
  });

  const user = meQuery.data ?? null;
  const isAuthenticated = Boolean(authToken) && Boolean(user);
  const isLoading = Boolean(authToken) && meQuery.isPending;

  const login = useCallback(
    async (email: string, code: string): Promise<boolean> => {
      try {
        const response = await verifyAuthCode(email, code);
        sessionStorage.setItem(AUTH_TOKEN_KEY, response.token);
        sessionStorage.setItem(USER_EMAIL_KEY, response.user.email);
        setAuthToken(response.token);
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
        sessionStorage.setItem(AUTH_TOKEN_KEY, response.token);
        sessionStorage.setItem(USER_EMAIL_KEY, response.user.email);
        setAuthToken(response.token);
        return true;
      } catch {
        return false;
      }
    },
    [],
  );

  const logout = useCallback(() => {
    sessionStorage.removeItem(AUTH_TOKEN_KEY);
    sessionStorage.removeItem(USER_EMAIL_KEY);
    setAuthToken(null);
    queryClient.clear();
  }, [queryClient]);

  return (
    <AuthContext.Provider value={{ isAuthenticated, user, isLoading, login, devLogin, logout }}>
      {children}
    </AuthContext.Provider>
  );
}
