import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { UserViewModel } from '../../api/generated/models';
import { AuthContext, type AuthContextType } from '../../providers/authContextValue';
import { ThemeProvider } from '../../providers/ThemeProvider';
import { AppLayout } from './AppLayout';

const superadmin: UserViewModel = {
  id: 'superadmin-id',
  organizationId: 'platform-organization',
  email: 'superadmin@workslip.dk',
  displayName: 'Super Admin',
  phone: '',
  role: 'Superadmin',
  roleDisplayName: 'Superadministrator',
  hoursThisWeek: null,
  hoursThisMonth: null,
  hoursBiweekly: null,
};

describe('AppLayout desktop-only defense', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.stubGlobal('navigator', {
      userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9)',
      maxTouchPoints: 5,
    });
  });

  it('blocks a mobile Superadmin even without the route boundary', () => {
    const value: AuthContextType = {
      hasAuthToken: true,
      isAuthenticated: true,
      user: superadmin,
      isLoading: false,
      login: vi.fn(),
      devLogin: vi.fn(),
      logout: vi.fn(),
      clearLocalSession: vi.fn(),
      updateUser: vi.fn(),
      meQuery: {
        isPending: false,
        isError: false,
        refetch: vi.fn(),
        data: superadmin,
      },
    };

    render(
      <MemoryRouter initialEntries={['/app']}>
        <ThemeProvider>
          <AuthContext.Provider value={value}>
            <AppLayout />
          </AuthContext.Provider>
        </ThemeProvider>
      </MemoryRouter>,
    );

    expect(screen.getByRole('heading', {
      name: 'Superadmin er kun tilgængelig på computer',
    })).toBeInTheDocument();
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  });
});
