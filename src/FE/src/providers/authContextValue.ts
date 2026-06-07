import { createContext } from 'react';
import type { UserViewModel } from '../api/generated/models';

export const AUTH_TOKEN_KEY = 'authToken';
export const USER_EMAIL_KEY = 'userEmail';

export interface AuthContextType {
  isAuthenticated: boolean;
  user: UserViewModel | null;
  isLoading: boolean;
  login: (email: string, code: string) => Promise<boolean>;
  devLogin: (email: string) => Promise<boolean>;
  logout: () => void;
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined);
