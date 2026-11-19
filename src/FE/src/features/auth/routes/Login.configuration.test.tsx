import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({
    hasAuthToken: false,
    isAuthenticated: false,
    isLoading: false,
    user: null,
    establishSession: vi.fn(),
  }),
}));

async function renderLogin() {
  const { Login } = await import('./Login');
  const router = createMemoryRouter([{ path: '/login', element: <Login /> }], {
    initialEntries: ['/login'],
  });
  render(<RouterProvider router={router} />);
}

beforeEach(() => {
  vi.resetModules();
  vi.stubEnv('DEV', true);
  vi.stubEnv('VITE_DEMO_MODE', 'false');
  vi.stubEnv('VITE_AZURE_AD_TENANT_ID', '');
  vi.stubEnv('VITE_AZURE_AD_CLIENT_ID', '');
  vi.stubEnv('VITE_AZURE_AD_SCOPE', '');
  localStorage.clear();
  sessionStorage.clear();
  document.documentElement.removeAttribute('data-auth-transition');
  window.history.replaceState(null, '', '/login');
});

afterEach(() => {
  cleanup();
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe('Login environment configuration', () => {
  it.each([
    { name: 'absent', tenant: '', client: '', scope: '' },
    { name: 'missing tenant', tenant: '', client: 'synthetic-client', scope: 'api://synthetic-client/access_as_user' },
    { name: 'missing client', tenant: 'synthetic-tenant', client: '', scope: 'api://synthetic-client/access_as_user' },
    { name: 'missing scope', tenant: 'synthetic-tenant', client: 'synthetic-client', scope: '' },
    { name: 'blank', tenant: ' ', client: ' ', scope: ' ' },
  ])('offers local developer roles when Entra settings are $name', async ({ tenant, client, scope }) => {
    vi.stubEnv('VITE_AZURE_AD_TENANT_ID', tenant);
    vi.stubEnv('VITE_AZURE_AD_CLIENT_ID', client);
    vi.stubEnv('VITE_AZURE_AD_SCOPE', scope);

    await renderLogin();

    expect(screen.getByRole('heading', { name: 'Lokal Workslip' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Log ind med Microsoft passkey' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Mistet dit login? Modtag engangskode' })).not.toBeInTheDocument();
    for (const { role, id } of [
      { role: 'User', id: 'login-dev-user' },
      { role: 'Auditor', id: 'login-dev-auditor' },
      { role: 'Admin', id: 'login-dev-admin' },
      { role: 'Superadmin', id: 'login-dev-superadmin' },
    ]) {
      const button = screen.getByRole('button', { name: `Dev Login · ${role}` });
      expect(button).toBeEnabled();
      expect(button).toHaveAttribute('id', id);
    }
  });

  it('offers Microsoft and OTC login when all public Entra settings are configured', async () => {
    vi.stubEnv('VITE_AZURE_AD_TENANT_ID', 'synthetic-tenant');
    vi.stubEnv('VITE_AZURE_AD_CLIENT_ID', 'synthetic-client');
    vi.stubEnv('VITE_AZURE_AD_SCOPE', 'api://synthetic-client/access_as_user');

    await renderLogin();

    expect(screen.getByRole('heading', { name: 'Log ind på Workslip' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toHaveAttribute('id', 'login-microsoft');
    expect(screen.getByRole('button', { name: 'Mistet dit login? Modtag engangskode' })).toHaveAttribute('id', 'login-otc');
  });

  it('surfaces the real Microsoft configuration error outside development without offering dev login', async () => {
    vi.stubEnv('DEV', false);
    const fetchSpy = vi.spyOn(globalThis, 'fetch');

    await renderLogin();

    expect(screen.getByRole('heading', { name: 'Log ind på Workslip' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Dev Login/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Log ind med Microsoft passkey' }));

    expect(await screen.findByText(/Microsoft login mangler konfiguration/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toBeEnabled();
    expect(document.documentElement).not.toHaveAttribute('data-auth-transition');
    expect(fetchSpy).not.toHaveBeenCalled();
  });
});
