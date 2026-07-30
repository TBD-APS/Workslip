import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import type { UserViewModel } from '../../../api/generated/models';
import { AuthContext, type AuthContextType } from '../../../providers/authContextValue';
import { SuperAdmin } from '../routes/SuperAdmin';
import { DesktopOnlySuperadminBoundary } from './DesktopOnlySuperadmin';
import {
  createOrganization,
  createOrganizationSession,
  getOrganizations,
  inviteOrganizationAdmin,
} from '../api';

vi.mock('../api', () => ({
  createOrganization: vi.fn(),
  createOrganizationSession: vi.fn(),
  getOrganizations: vi.fn(),
  getSuperadminErrorMessage: () => 'Fejl',
  inviteOrganizationAdmin: vi.fn(),
  superadminOrganizationQueryKey: ['superadmin', 'organizations'],
}));

const superadmin: UserViewModel = {
  id: 'superadmin-id',
  organizationId: 'platform-organization',
  email: 'superadmin@workslip.dk',
  displayName: 'Super Admin',
  phone: '',
  role: 'Superadmin',
  hoursThisWeek: null,
  hoursThisMonth: null,
  hoursBiweekly: null,
};

function useDevice(device: 'mobile' | 'desktop'): void {
  vi.stubGlobal('navigator', device === 'mobile'
    ? {
      userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)',
      maxTouchPoints: 5,
    }
    : {
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
      maxTouchPoints: 0,
    });
}

function renderWithAuth(
  children: ReactNode,
  user: UserViewModel = superadmin,
): { logout: ReturnType<typeof vi.fn> } {
  const logout = vi.fn();
  const value: AuthContextType = {
    hasAuthToken: true,
    isAuthenticated: true,
    user,
    isLoading: false,
    login: vi.fn(),
    devLogin: vi.fn(),
    logout,
    clearLocalSession: vi.fn(),
    updateUser: vi.fn(),
    meQuery: {
      isPending: false,
      isError: false,
      refetch: vi.fn(),
      data: user,
    },
  };
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  render(
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
    </QueryClientProvider>,
  );

  return { logout };
}

describe('DesktopOnlySuperadminBoundary', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    vi.unstubAllGlobals();
  });

  it('blocks mobile before organization code or requests run', () => {
    useDevice('mobile');
    const { logout } = renderWithAuth(
      <DesktopOnlySuperadminBoundary>
        <SuperAdmin />
      </DesktopOnlySuperadminBoundary>,
    );

    expect(screen.getByRole('heading', {
      name: 'Superadmin er kun tilgængelig på computer',
    })).toBeInTheDocument();
    expect(getOrganizations).not.toHaveBeenCalled();
    expect(createOrganization).not.toHaveBeenCalled();
    expect(inviteOrganizationAdmin).not.toHaveBeenCalled();
    expect(createOrganizationSession).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Log ud' }));
    expect(logout).toHaveBeenCalledOnce();
  });

  it('allows the desktop boundary path', () => {
    useDevice('desktop');
    renderWithAuth(
      <DesktopOnlySuperadminBoundary>
        <div>Superadmin desktop</div>
      </DesktopOnlySuperadminBoundary>,
    );

    expect(screen.getByText('Superadmin desktop')).toBeInTheDocument();
  });

  it('does not block an ordinary mobile user', () => {
    useDevice('mobile');
    renderWithAuth(
      <DesktopOnlySuperadminBoundary>
        <div>Normal mobilapp</div>
      </DesktopOnlySuperadminBoundary>,
      { ...superadmin, role: 'User' },
    );

    expect(screen.getByText('Normal mobilapp')).toBeInTheDocument();
  });
});
