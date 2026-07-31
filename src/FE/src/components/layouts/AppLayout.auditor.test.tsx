import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import type { UserViewModel } from '../../api/generated/models';
import { AuthContext, type AuthContextType } from '../../providers/authContextValue';
import { ThemeProvider } from '../../providers/ThemeProvider';
import { AppLayout } from './AppLayout';

const auditor: UserViewModel = {
  id: 'auditor-id',
  organizationId: 'organization-id',
  email: 'auditor@workslip.dk',
  displayName: 'Auditor',
  phone: '',
  role: 'Auditor',
  roleDisplayName: 'Auditor',
  hoursThisWeek: null,
  hoursThisMonth: null,
  hoursBiweekly: null,
};

function renderAuditor(initialPath: string) {
  const value: AuthContextType = {
    hasAuthToken: true,
    isAuthenticated: true,
    user: auditor,
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
      data: auditor,
    },
  };

  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <ThemeProvider>
        <AuthContext.Provider value={value}>
          <Routes>
            <Route path="/app" element={<AppLayout />}>
              <Route path="auditor" element={<div>Rapportoversigt</div>} />
            </Route>
          </Routes>
        </AuthContext.Provider>
      </ThemeProvider>
    </MemoryRouter>,
  );
}

describe('AppLayout Auditor session', () => {
  it('redirects the generic app home to the report overview', async () => {
    renderAuditor('/app');

    expect(await screen.findByText('Rapportoversigt')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Rapporter' })).toHaveAttribute('href', '/app/auditor');
  });

  it('does not render notification controls that Auditor cannot use', () => {
    renderAuditor('/app/auditor');

    expect(screen.queryByRole('button', { name: /Notifikationer/ })).not.toBeInTheDocument();
  });
});
