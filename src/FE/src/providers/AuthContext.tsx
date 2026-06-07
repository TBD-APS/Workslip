import { createContext, useCallback, useContext, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { verifyAuthCode, getDevToken } from '../features/auth/api/devToken';
import { getGetApiAuthMeQueryKey, useGetApiAuthMe } from '../api/generated/auth/auth';
import type { UserViewModel } from '../api/generated/models';
import { getResponseData } from '../lib/unwrapResponse';

const AUTH_TOKEN_KEY = 'authToken';
const USER_EMAIL_KEY = 'userEmail';

interface AuthContextType {
  isAuthenticated: boolean;
  user: UserViewModel | null;
  isLoading: boolean;
  login: (email: string, code: string) => Promise<boolean>;
  devLogin: (email: string) => Promise<boolean>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [authToken, setAuthToken] = useState<string | null>(() => localStorage.getItem(AUTH_TOKEN_KEY));
  const queryClient = useQueryClient();

  const meQuery = useGetApiAuthMe({
    query: {
      enabled: Boolean(authToken),
      retry: false,
      staleTime: 5 * 60 * 1000,
    },
  });

  const user = getResponseData<UserViewModel>(meQuery.data) ?? null;
  const isAuthenticated = Boolean(authToken) && Boolean(user);
  const isLoading = Boolean(authToken) && meQuery.isPending;

  const login = useCallback(
    async (email: string, code: string): Promise<boolean> => {
      try {
        const response = await verifyAuthCode(email, code);
        localStorage.setItem(AUTH_TOKEN_KEY, response.token);
        localStorage.setItem(USER_EMAIL_KEY, response.user.email);
        setAuthToken(response.token);
        queryClient.invalidateQueries({ queryKey: getGetApiAuthMeQueryKey() });
        return true;
      } catch {
        return false;
      }
    },
    [queryClient],
  );

  const devLogin = useCallback(
    async (email: string): Promise<boolean> => {
      try {
        const response = await getDevToken(email);
        localStorage.setItem(AUTH_TOKEN_KEY, response.token);
        localStorage.setItem(USER_EMAIL_KEY, response.user.email);
        setAuthToken(response.token);
        queryClient.invalidateQueries({ queryKey: getGetApiAuthMeQueryKey() });
        return true;
      } catch {
        return false;
      }
    },
    [queryClient],
  );

  const logout = useCallback(() => {
    localStorage.removeItem(AUTH_TOKEN_KEY);
    localStorage.removeItem(USER_EMAIL_KEY);
    setAuthToken(null);
    queryClient.invalidateQueries({ queryKey: getGetApiAuthMeQueryKey() });
  }, [queryClient]);

  return (
    <AuthContext.Provider value={{ isAuthenticated, user, isLoading, login, devLogin, logout }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
