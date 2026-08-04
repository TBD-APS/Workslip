import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
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

const authValue: AuthContextType = {
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

function FocusHarness({ showHours = true }: { showHours?: boolean }) {
  return (
    <div>
      {showHours && <input aria-label="Timer" inputMode="decimal" />}
      <textarea aria-label="Kommentar" />
      <input aria-label="Bekræft" type="checkbox" />
      <button type="button">Færdig</button>
    </div>
  );
}

function TestApp({ showHours = true }: { showHours?: boolean }) {
  return (
    <MemoryRouter initialEntries={['/app/auditor']}>
      <ThemeProvider>
        <AuthContext.Provider value={authValue}>
          <Routes>
            <Route path="/app" element={<AppLayout />}>
              <Route path="auditor" element={<FocusHarness showHours={showHours} />} />
            </Route>
          </Routes>
        </AuthContext.Provider>
      </ThemeProvider>
    </MemoryRouter>
  );
}

afterEach(() => {
  cleanup();
});

describe('AppLayout text-entry focus', () => {
  it('hides the bottom navigation while a text field is focused and restores it on blur', async () => {
    const { container } = render(<TestApp />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;
    const hours = screen.getByRole('textbox', { name: 'Timer' });

    act(() => hours.focus());
    expect(shell).toHaveClass('keyboard-visible');

    act(() => hours.blur());
    await waitFor(() => expect(shell).not.toHaveClass('keyboard-visible'));
  });

  it('keeps the navigation hidden while focus moves directly between text fields', () => {
    const { container } = render(<TestApp />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;

    act(() => screen.getByRole('textbox', { name: 'Timer' }).focus());
    expect(shell).toHaveClass('keyboard-visible');

    act(() => screen.getByRole('textbox', { name: 'Kommentar' }).focus());
    expect(shell).toHaveClass('keyboard-visible');
  });

  it('dismisses text-entry focus when the user taps a non-editable control', async () => {
    const { container } = render(<TestApp />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;
    const hours = screen.getByRole('textbox', { name: 'Timer' });

    act(() => hours.focus());
    expect(shell).toHaveClass('keyboard-visible');

    fireEvent.pointerDown(screen.getByRole('button', { name: 'Færdig' }));

    expect(document.activeElement).not.toBe(hours);
    await waitFor(() => expect(shell).not.toHaveClass('keyboard-visible'));
  });

  it('restores the navigation if a focused field is removed without a normal focus transition', async () => {
    const view = render(<TestApp />);
    const shell = view.container.querySelector<HTMLElement>('.app-shell')!;
    const hours = screen.getByRole('textbox', { name: 'Timer' });

    act(() => hours.focus());
    expect(shell).toHaveClass('keyboard-visible');

    view.rerender(<TestApp showHours={false} />);

    await waitFor(() => expect(shell).not.toHaveClass('keyboard-visible'));
  });

  it('does not hide the navigation for non-text inputs', () => {
    const { container } = render(<TestApp />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;

    act(() => screen.getByRole('checkbox', { name: 'Bekræft' }).focus());

    expect(shell).not.toHaveClass('keyboard-visible');
  });
});
