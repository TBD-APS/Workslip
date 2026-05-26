import {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef,
  type ReactNode,
} from 'react';
import { useAuthRequest, useAutoDiscovery, exchangeCodeAsync } from 'expo-auth-session';
import * as SecureStore from 'expo-secure-store';

import { azureConfig, scopes, redirectUri, getDiscoveryUrl } from './config';
{
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef,
  type ReactNode,
} from 'react';
import { useAuthRequest, useAutoDiscovery, exchangeCodeAsync } from 'expo-auth-session';
import * as SecureStore from 'expo-secure-store';

import { azureConfig, scopes, redirectUri, getDiscoveryUrl } from './config';

type User = {
  sub: string;
  name: string;
  preferred_username: string;
  email?: string;
};

type Tokens = {
  accessToken: string;
  refreshToken?: string;
};

type AuthState = {
  user: User | null;
  accessToken: string | null;
  isLoading: boolean;
  isAuthenticated: boolean;
};

type AuthContextType = AuthState & {
  login: () => Promise<void>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextType | null>(null);

const TOKEN_KEY = 'auth_tokens';
const USER_KEY = 'auth_user';

function storeTokens(tokens: Tokens) {
  return SecureStore.setItemAsync(TOKEN_KEY, JSON.stringify(tokens));
}

async function getStoredTokens(): Promise<Tokens | null> {
  const raw = await SecureStore.getItemAsync(TOKEN_KEY);
  return raw ? JSON.parse(raw) : null;
}

function storeUser(user: User) {
  return SecureStore.setItemAsync(USER_KEY, JSON.stringify(user));
}

async function getStoredUser(): Promise<User | null> {
  const raw = await SecureStore.getItemAsync(USER_KEY);
  return raw ? JSON.parse(raw) : null;
}

function clearStorage() {
  return Promise.all([
    SecureStore.deleteItemAsync(TOKEN_KEY),
    SecureStore.deleteItemAsync(USER_KEY),
  ]);
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({
    user: null,
    accessToken: null,
    isLoading: true,
    isAuthenticated: false,
  });

  const discovery = useAutoDiscovery(getDiscoveryUrl(azureConfig.tenantId));
  const [request, response, promptAsync] = useAuthRequest(
    {
      clientId: azureConfig.clientId,
      scopes,
      redirectUri,
      usePKCE: true,
    },
    discovery
  );

  const tokensRef = useRef<Tokens | null>(null);

  useEffect(() => {
    (async () => {
      const stored = await getStoredTokens();
      const user = await getStoredUser();
      if (stored && user) {
        tokensRef.current = stored;
        setState({
          user,
          accessToken: stored.accessToken,
          isLoading: false,
          isAuthenticated: true,
        });
      } else {
        setState((s) => ({ ...s, isLoading: false }));
      }
    })();
  }, []);

  useEffect(() => {
    if (response?.type !== 'success') return;

    (async () => {
      let tokens: Tokens | null = null;

      if (response.authentication?.accessToken) {
        tokens = {
          accessToken: response.authentication.accessToken,
          refreshToken: response.authentication.refreshToken,
        };
      } else if (response.params?.code && request?.codeVerifier && discovery?.tokenEndpoint) {
        const tokenResp = await exchangeCodeAsync(
          {
            code: response.params.code,
            clientId: azureConfig.clientId,
            redirectUri,
            extraParams: { code_verifier: request.codeVerifier },
          },
          { tokenEndpoint: discovery.tokenEndpoint }
        );
        tokens = {
          accessToken: tokenResp.accessToken,
          refreshToken: tokenResp.refreshToken,
        };
      }

      if (!tokens) return;

      const res = await fetch(`https://graph.microsoft.com/v1.0/me`, {
        headers: { Authorization: `Bearer ${tokens.accessToken}` },
      });
      const data = await res.json();

      const user: User = {
        sub: data.id,
        name: data.displayName,
        preferred_username: data.userPrincipalName,
        email: data.mail ?? undefined,
      };

      tokensRef.current = tokens;
      await Promise.all([storeTokens(tokens), storeUser(user)]);
      setState({
        user,
        accessToken: tokens.accessToken,
        isLoading: false,
        isAuthenticated: true,
      });
    })();
  }, [response, request?.codeVerifier, discovery?.tokenEndpoint]);

  const login = useCallback(async () => {
    await promptAsync();
  }, [promptAsync]);

  const logout = useCallback(async () => {
    tokensRef.current = null;
    await clearStorage();
    tokensRef.current = null;
    await clearStorage();
    setState({
      user: null,
      accessToken: null,
      isLoading: false,
      isAuthenticated: false,
    });
  }, []);

  return (
    <AuthContext.Provider value={{ ...state, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextType {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
