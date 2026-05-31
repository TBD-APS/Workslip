import { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { verifyAuthCode, getDevToken } from '../features/auth/api/devToken';

export interface AuthUser {
  email: string;
}

interface AuthContextType {
  isAuthenticated: boolean;
  user: AuthUser | null;
  isLoading: boolean;
  login: (email: string, code: string) => Promise<boolean>;
  devLogin: (email: string) => Promise<boolean>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    // Check on mount if user is already logged in
    const token = localStorage.getItem('authToken');
    const storedEmail = localStorage.getItem('userEmail');
    if (token && storedEmail) {
      setIsAuthenticated(true);
      setUser({ email: storedEmail });
    }
    setIsLoading(false);
  }, []);

  const login = useCallback(async (email: string, code: string): Promise<boolean> => {
    try {
      const response = await verifyAuthCode(email, code);
      localStorage.setItem('authToken', response.token);
      localStorage.setItem('userEmail', response.user.email);
      setIsAuthenticated(true);
      setUser(response.user);
      return true;
    } catch {
      setIsAuthenticated(false);
      setUser(null);
      return false;
    }
  }, []);

  const devLogin = useCallback(async (email: string): Promise<boolean> => {
    try {
      const response = await getDevToken(email);
      localStorage.setItem('authToken', response.token);
      localStorage.setItem('userEmail', response.user.email);
      setIsAuthenticated(true);
      setUser(response.user);
      return true;
    } catch {
      setIsAuthenticated(false);
      setUser(null);
      return false;
    }
  }, []);

  const logout = useCallback(() => {
    localStorage.removeItem('authToken');
    localStorage.removeItem('userEmail');
    setIsAuthenticated(false);
    setUser(null);
  }, []);

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
