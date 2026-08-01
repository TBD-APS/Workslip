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
    data: { invites: [] },
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
});
