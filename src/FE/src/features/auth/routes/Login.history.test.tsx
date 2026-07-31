import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Login } from './Login';

const { authState, entraMocks } = vi.hoisted(() => ({
  authState: { isAuthenticated: true },
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
    isAuthenticated: authState.isAuthenticated,
    devLogin: vi.fn(),
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
    ],
    { initialEntries, initialIndex },
  );

  render(<RouterProvider router={router} />);
  return router;
}

beforeEach(() => {
  authState.isAuthenticated = true;
  window.history.replaceState(null, '', '/login');
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
  it('replaces the login entry when an authenticated user is redirected', async () => {
    const router = renderLogin(['/before', '/login'], 1);
    expect(await screen.findByText('App')).toBeInTheDocument();

    await act(async () => {
      await router.navigate(-1);
    });

    expect(await screen.findByText('Før login')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/before');
  });

  it('recovers an interactive login restored from the back-forward cache', async () => {
    authState.isAuthenticated = false;
    entraMocks.startEntraLogin.mockImplementation(() => new Promise<void>(() => {}));
    renderLogin();

    fireEvent.click(screen.getByRole('button', { name: 'Log ind med Microsoft passkey' }));
    expect(await screen.findByText('Sender til Microsoft...')).toBeInTheDocument();

    entraMocks.hasEntraLoginSession.mockReturnValue(true);
    const pageShow = new Event('pageshow') as PageTransitionEvent;
    Object.defineProperty(pageShow, 'persisted', { value: true });
    act(() => window.dispatchEvent(pageShow));

    expect(await screen.findByText('Login afbrudt. Klik på knappen for at prøve igen.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toBeEnabled();
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
});
