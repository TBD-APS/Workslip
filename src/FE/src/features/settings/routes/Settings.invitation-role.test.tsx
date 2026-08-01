import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Settings } from './Settings';

const { inviteMutation, invalidateQueries } = vi.hoisted(() => ({
  inviteMutation: vi.fn(),
  invalidateQueries: vi.fn(),
}));

vi.mock('@tanstack/react-query', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-query')>('@tanstack/react-query');
  return {
    ...actual,
    useQueryClient: () => ({ invalidateQueries }),
  };
});

vi.mock('../../../api/generated/auth/auth', () => ({
  usePostApiAuthInvite: () => ({
    mutateAsync: inviteMutation,
    isPending: false,
  }),
}));

vi.mock('../api', () => ({
  useGetApiAuthInvites: () => ({
    isLoading: false,
    isError: false,
    data: {
      invites: [{
        id: 'invite-1',
        email: 'very.long.invitation.email@example.com',
        role: 'User',
        createdAt: '2026-08-01T08:00:00Z',
        expiresAt: '2099-08-01T08:00:00Z',
        consumed: false,
        openedAt: null,
        acceptedAt: null,
      }],
    },
  }),
  useDeleteApiAuthInvite: () => ({
    mutateAsync: vi.fn(),
    isPending: false,
  }),
}));

vi.mock('../../../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

afterEach(() => {
  cleanup();
  inviteMutation.mockReset();
  inviteMutation.mockResolvedValue({ results: [] });
  invalidateQueries.mockReset();
});

describe('Settings invitation role', () => {
  it('defaults to User and sends the selected Auditor role', async () => {
    render(
      <MemoryRouter>
        <Settings />
      </MemoryRouter>,
    );

    const roleSelect = screen.getByRole('combobox', { name: 'Rolle for invitationerne' });
    expect(roleSelect).toHaveValue('User');
    expect(screen.getByRole('option', { name: 'Medarbejder' })).toHaveValue('User');
    expect(screen.getByRole('option', { name: 'Auditør' })).toHaveValue('Auditor');

    fireEvent.change(roleSelect, { target: { value: 'Auditor' } });
    fireEvent.change(screen.getByPlaceholderText('Skriv e-mail...'), {
      target: { value: 'auditor@example.com' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Tilføj e-mail' }));
    fireEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

    await waitFor(() => {
      expect(inviteMutation).toHaveBeenCalledWith({
        data: {
          emails: ['auditor@example.com'],
          role: 'Auditor',
          inviteBaseUrl: window.location.origin,
        },
      });
    });
  });

  it('uses the compact User role label while exposing the full role and e-mail', () => {
    render(
      <MemoryRouter>
        <Settings />
      </MemoryRouter>,
    );

    expect(screen.getByText('Medarb.')).toHaveAttribute('aria-hidden', 'true');
    expect(screen.getByText('Rolle: Medarbejder')).toHaveClass('invite-role-full-label');
    expect(screen.getByText('Medarb.').parentElement).toHaveAttribute('title', 'Medarbejder');
    expect(screen.getByText('very.long.invitation.email@example.com')).toHaveAttribute(
      'title',
      'very.long.invitation.email@example.com',
    );
  });
});
