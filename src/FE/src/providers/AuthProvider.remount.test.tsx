import { useEffect } from 'react';
import { render, screen, act, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from './AuthContext';
import { useAuth } from './useAuth';
import type { AuthContextType } from './authContextValue';

// The lazy authenticated session module is replaced with a light stand-in that
// immediately reports an authenticated value. This keeps the test focused on the
// provider's mount behaviour rather than the identity query.
vi.mock('./AuthenticatedAppProvider', () => ({
  AuthenticatedSessionEffects: ({
    login,
    establishSession,
    clearSession,
    onValueChange,
  }: {
    login: AuthContextType['login'];
    establishSession: AuthContextType['establishSession'];
    clearSession: () => void;
    onValueChange: (value: AuthContextType) => void;
  }) => {
    useEffect(() => {
      onValueChange({
        hasAuthToken: true,
        isAuthenticated: true,
        user: { id: 'u1', email: 'user@example.test' } as AuthContextType['user'],
        isLoading: false,
        login,
        establishSession,
        logout: clearSession,
        clearLocalSession: clearSession,
        updateUser: () => undefined,
        meQuery: { isPending: false, isError: false, refetch: async () => null, data: null },
      });
    }, [login, establishSession, clearSession, onValueChange]);
    return null;
  },
}));

let mountCount = 0;

function MountProbe() {
  useEffect(() => {
    mountCount += 1;
  }, []);
  return <div data-testid="routed-app">routed app</div>;
}

function LoginTrigger() {
  const { establishSession } = useAuth();
  return (
    <button type="button" onClick={() => establishSession('token-123', 'user@example.test', 'user')}>
      sign in
    </button>
  );
}

describe('AuthProvider keeps the routed app mounted across login', () => {
  beforeEach(() => {
    mountCount = 0;
    localStorage.clear();
    document.documentElement.removeAttribute('data-auth-transition');
  });

  it('does not remount children when the auth token is established', async () => {
    render(
      <AuthProvider>
        <MountProbe />
        <LoginTrigger />
      </AuthProvider>,
    );

    expect(screen.getByTestId('routed-app')).toBeInTheDocument();
    expect(mountCount).toBe(1);

    await act(async () => {
      screen.getByText('sign in').click();
    });

    // The authenticated session module resolves and reports its value; the
    // routed app must stay the same instance throughout (no teardown/rebuild).
    await waitFor(() => {
      expect(document.documentElement).toHaveAttribute('data-auth-transition');
    });

    expect(screen.getByTestId('routed-app')).toBeInTheDocument();
    expect(mountCount).toBe(1);
  });
});
