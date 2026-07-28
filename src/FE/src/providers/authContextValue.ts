import { createContext } from 'react';
import type { UserViewModel } from '../api/generated/models';

export const AUTH_TOKEN_KEY = 'authToken';
export const USER_EMAIL_KEY = 'userEmail';
export const REAUTH_IN_FLIGHT_KEY = 'workslip.reauthInFlight';

/**
 * TTL on the reauthInFlight flag. If the redirect is interrupted (browser
 * crash, network drop before reaching /login), the flag would otherwise stay
 * set forever and silently break the next expiry cycle. 30s is well above
 * any realistic redirect duration and well below the next possible expiry
 * cycle (60 min JWT TTL), so a stale flag will always self-heal.
 */
const REAUTH_IN_FLIGHT_TTL_MS = 30_000;

/**
 * Centralized storage helper for auth tokens and short-lived OAuth flow state.
 *
 * Workslip is an installed PWA. `sessionStorage` is evicted by iOS/Android PWAs
 * on backgrounding and is gone when the user comes back to the app, which makes
 * the user look logged out at random. We persist the JWT (short-lived, 60 min
 * default) in `localStorage` so it survives PWA eviction and reload.
 *
 * SECURITY NOTE: `localStorage` is readable by any JavaScript that runs on our
 * origin. If a third-party script or vulnerable npm dependency is ever
 * injected, an attacker could steal the JWT and impersonate the user for up
 * to 60 minutes. The trade-off is intentional: PWA eviction with
 * `sessionStorage` is the dominant real-world logout cause. Future migrations
 * to HttpOnly cookies (set by the backend) or IndexedDB with a worker-held
 * encryption key would close this gap; both are larger architectural changes.
 *
 * Future migrations to IndexedDB / secure cookies are a single-file change.
 */
const authStorage = {
  getItem(key: string): string | null {
    return localStorage.getItem(key);
  },
  setItem(key: string, value: string): void {
    localStorage.setItem(key, value);
  },
  removeItem(key: string): void {
    localStorage.removeItem(key);
  },
};

export const AuthStorage = authStorage;

/**
 * Sets the reauthInFlight flag with a self-expiring TTL. Storing the expiry
 * timestamp alongside the flag lets any reader cheaply check whether the
 * flag is still valid without a separate sweep job. If the redirect is
 * interrupted before the user reaches /login, the next expiry cycle will
 * simply ignore the stale flag and re-trigger.
 */
export const setReauthInFlight = (): void => {
  const expiresAt = Date.now() + REAUTH_IN_FLIGHT_TTL_MS;
  AuthStorage.setItem(REAUTH_IN_FLIGHT_KEY, String(expiresAt));
};

/**
 * Returns true if a reauth redirect is currently in flight AND has not
 * exceeded its TTL. Callers should treat the flag as advisory — the only
 * purpose is to deduplicate concurrent 401-triggered redirects.
 */
export const isReauthInFlight = (): boolean => {
  const raw = AuthStorage.getItem(REAUTH_IN_FLIGHT_KEY);
  if (!raw) return false;
  const expiresAt = Number(raw);
  if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) {
    AuthStorage.removeItem(REAUTH_IN_FLIGHT_KEY);
    return false;
  }
  return true;
};

export const clearReauthInFlight = (): void => {
  AuthStorage.removeItem(REAUTH_IN_FLIGHT_KEY);
};

export interface AuthMeQuery {
  // Shape kept loose on purpose: the real type is ReturnType<typeof useGetApiAuthMe>
  // and including it here would force this file to depend on the generated API.
  // Callers only consume `isError`, `isPending`, `refetch`, and `data`.
  isPending: boolean;
  isError: boolean;
  refetch: () => Promise<unknown>;
  data?: UserViewModel | null;
}

export interface AuthContextType {
  hasAuthToken: boolean;
  isAuthenticated: boolean;
  user: UserViewModel | null;
  isLoading: boolean;
  login: (email: string, code: string) => Promise<boolean>;
  devLogin: (email: string) => Promise<boolean>;
  logout: () => void;
  updateUser: (partial: Partial<Pick<UserViewModel, 'displayName' | 'phone'>>) => void;
  meQuery: AuthMeQuery;
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined);
