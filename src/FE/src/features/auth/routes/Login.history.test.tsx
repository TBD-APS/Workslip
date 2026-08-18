import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Login } from './Login';

const { authState, authMocks, entraMocks } = vi.hoisted(() => ({
  authState: {
    hasAuthToken: false,
    isAuthenticated: true,
    isLoading: false,
    user: null as { role?: string } | null,
  },
  authMocks: {
    establishSession: vi.fn(),
  },
  entraMocks: {
    clearEntraLoginSession: vi.fn(),
    completeEntraLogin: vi.fn(),
    hasEntraLoginCallback: vi.fn(() => false),
    hasEntraLoginSession: vi.fn(() => false),
    sanitizeReturnTo: vi.fn((value: string | null) => value ?? '/app'),
    startEntraLogin: vi.fn(),
  },
}));

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({
    hasAuthToken: authState.hasAuthToken,
    isAuthenticated: authState.isAuthenticated,
    isLoading: authState.isLoading,
    user: authState.user,
    establishSession: authMocks.establishSession,
  }),
}));

vi.mock('../api/entraLogin', () => ({
  ...entraMocks,
  InteractiveLoginRequiredError: class InteractiveLoginRequiredError extends Error {},
  LoginCancelledError: class LoginCancelledError extends Error {},
}));

function renderLogin(initialEntries: string[] = ['/login'], initialIndex = initialEntries.length - 1) {
  const router = createMemoryRouter(
    [
      { path: '/before', element: <div>Før login</div> },
      { path: '/login', element: <Login /> },
      { path: '/app', element: <div>App</div> },
      { path: '/app/overblik', element: <div>Overblik</div> },
      { path: '/superadmin', element: <div>Superadmin</div> },
    ],
    { initialEntries, initialIndex },
  );

  render(<RouterProvider router={router} />);
  return router;
}

beforeEach(() => {
  authState.hasAuthToken = false;
  authState.isAuthenticated = true;
  authState.isLoading = false;
  authState.user = null;
  localStorage.clear();
  document.documentElement.removeAttribute('data-auth-transition');
  window.history.replaceState(null, '', '/login');
  authMocks.establishSession.mockReset();
  entraMocks.clearEntraLoginSession.mockReset();
  entraMocks.completeEntraLogin.mockReset();
  entraMocks.hasEntraLoginCallback.mockReset().mockReturnValue(false);
  entraMocks.hasEntraLoginSession.mockReset().mockReturnValue(false);
  entraMocks.sanitizeReturnTo.mockReset().mockImplementation((value: string | null) => value ?? '/app');
  entraMocks.startEntraLogin.mockReset().mockResolvedValue(undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('Login browser history handling', () => {
  it('replaces the login entry and sends an authenticated user to Overblik', async () => {
    const router = renderLogin(['/before', '/login'], 1);
    expect(await screen.findByText('Overblik')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/app/overblik');

    await act(async () => {
      await router.navigate(-1);
    });

    expect(await screen.findByText('Før login')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/before');
  });

  it('keeps a stored session on the auth loading surface while identity is pending', async () => {
    authState.hasAuthToken = true;
    authState.isAuthenticated = false;
    authState.isLoading = true;

    renderLogin();

    expect(await screen.findByText('Tjekker login')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Log ind med Microsoft passkey' })).not.toBeInTheDocument();
  });

  it('keeps the Microsoft callback on the auth loading surface and routes Superadmin directly', async () => {
    authState.isAuthenticated = false;
    entraMocks.hasEntraLoginCallback.mockReturnValue(true);

    const callbackResult = {
      auth: {
        token: 'token',
        tokenType: 'Bearer',
        expiresIn: 3600,
        user: {
          userId: 'user-1',
          organizationId: 'organization-1',
          email: 'superadmin@example.com',
          displayName: 'Superadmin',
          role: 'Superadmin',
        },
      },
      returnTo: '/app',
    };
    let finishCallback!: () => void;
    entraMocks.completeEntraLogin.mockImplementation(() => new Promise((resolve) => {
      finishCallback = () => resolve(callbackResult);
    }));
    window.history.replaceState(null, '', '/login?code=callback&state=state');

    const router = renderLogin(['/login?code=callback&state=state']);

    expect(await screen.findByText('Tjekker login')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Log ind med Microsoft passkey' })).not.toBeInTheDocument();
    expect(document.documentElement).toHaveAttribute('data-auth-transition');

    await act(async () => {
      finishCallback();
    });

    await waitFor(() => expect(authMocks.establishSession).toHaveBeenCalledWith(
      'token',
      'superadmin@example.com',
      'Superadmin',
    ));
    await waitFor(() => expect(router.state.location.pathname).toBe('/superadmin'));
    expect(await screen.findByText('Superadmin')).toBeInTheDocument();
    expect(entraMocks.clearEntraLoginSession).toHaveBeenCalled();
  });

  it('recovers an interactive login restored from the back-forward cache', async () => {
    authState.isAuthenticated = false;
    entraMocks.startEntraLogin.mockImplementation(() => new Promise<void>(() => {}));
    renderLogin();

    fireEvent.click(screen.getByRole('button', { name: 'Log ind med Microsoft passkey' }));
    expect(await screen.findByText('Tjekker login')).toBeInTheDocument();
    expect(document.documentElement).toHaveAttribute('data-auth-transition');

    entraMocks.hasEntraLoginSession.mockReturnValue(true);
    const pageShow = new Event('pageshow') as PageTransitionEvent;
    Object.defineProperty(pageShow, 'persisted', { value: true });
    act(() => window.dispatchEvent(pageShow));

    expect(await screen.findByText('Login afbrudt. Klik på knappen for at prøve igen.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toBeEnabled();
    expect(document.documentElement).not.toHaveAttribute('data-auth-transition');
    expect(entraMocks.clearEntraLoginSession).toHaveBeenCalled();
  });

  it('does not restart reauthentication on a non-cached history restore', async () => {
    authState.isAuthenticated = false;
    entraMocks.hasEntraLoginSession.mockReturnValue(true);
    vi.spyOn(performance, 'getEntriesByType').mockReturnValue([
      { type: 'back_forward' } as PerformanceNavigationTiming,
    ]);
    window.history.replaceState(null, '', '/login?reauth=1&returnTo=%2Fapp%2Fcustomers');

    renderLogin(['/login?reauth=1&returnTo=%2Fapp%2Fcustomers']);

    expect(await screen.findByText('Login afbrudt. Klik på knappen for at prøve igen.')).toBeInTheDocument();
    await waitFor(() => expect(entraMocks.clearEntraLoginSession).toHaveBeenCalled());
    expect(entraMocks.startEntraLogin).not.toHaveBeenCalled();
    expect(window.location.pathname).toBe('/login');
    expect(window.location.search).toBe('');
    expect(screen.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toBeEnabled();
  });

  it('uses the stored user email as a hint for silent reauthentication', async () => {
    authState.isAuthenticated = false;
    localStorage.setItem('userEmail', 'known.user@example.com');
    window.history.replaceState(null, '', '/login?reauth=1&returnTo=%2Fapp%2Fcustomers');

    renderLogin(['/login?reauth=1&returnTo=%2Fapp%2Fcustomers']);

    await waitFor(() => expect(entraMocks.startEntraLogin).toHaveBeenCalledWith({
      returnTo: '/app/customers',
      prompt: 'none',
      loginHint: 'known.user@example.com',
    }));
  });

  it('skips silent reauthentication when no stored user email is available', async () => {
    authState.isAuthenticated = false;
    window.history.replaceState(null, '', '/login?reauth=1&returnTo=%2Fapp%2Fcustomers');

    renderLogin(['/login?reauth=1&returnTo=%2Fapp%2Fcustomers']);

    await waitFor(() => expect(entraMocks.startEntraLogin).toHaveBeenCalledWith({
      returnTo: '/app/customers',
      prompt: 'select_account',
    }));
    expect(entraMocks.startEntraLogin).not.toHaveBeenCalledWith(expect.objectContaining({ prompt: 'none' }));
  });
});
