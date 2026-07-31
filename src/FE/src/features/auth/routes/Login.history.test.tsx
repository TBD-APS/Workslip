import { act, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { Login } from './Login';

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    devLogin: vi.fn(),
  }),
}));

vi.mock('../api/entraLogin', () => ({
  clearEntraLoginSession: vi.fn(),
  completeEntraLogin: vi.fn(),
  hasEntraLoginCallback: vi.fn(() => false),
  InteractiveLoginRequiredError: class InteractiveLoginRequiredError extends Error {},
  LoginCancelledError: class LoginCancelledError extends Error {},
  sanitizeReturnTo: vi.fn((value: string | null) => value ?? '/app'),
  startEntraLogin: vi.fn(),
}));

describe('Login browser history handling', () => {
  it('replaces the login entry when an authenticated user is redirected', async () => {
    const router = createMemoryRouter(
      [
        { path: '/before', element: <div>Før login</div> },
        { path: '/login', element: <Login /> },
        { path: '/app', element: <div>App</div> },
      ],
      {
        initialEntries: ['/before', '/login'],
        initialIndex: 1,
      },
    );

    render(<RouterProvider router={router} />);

    expect(await screen.findByText('App')).toBeInTheDocument();

    await act(async () => {
      await router.navigate(-1);
    });

    expect(await screen.findByText('Før login')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/before');
  });
});
