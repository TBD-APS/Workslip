import type { PropsWithChildren } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SuperAdminUsersPanel } from './SuperAdminUsersPanel';

const api = vi.hoisted(() => ({
  getUsers: vi.fn(),
  getOptions: vi.fn(),
  createUser: vi.fn(),
  updateUser: vi.fn(),
  deleteUser: vi.fn(),
}));

vi.mock('../api', () => ({
  getSuperadminUsers: api.getUsers,
  getSuperadminUserOptions: api.getOptions,
  createSuperadminUser: api.createUser,
  updateSuperadminUser: api.updateUser,
  deleteSuperadminUser: api.deleteUser,
  getSuperadminErrorMessage: () => 'Fejl',
  superadminUserQueryKey: ['superadmin', 'users'],
  superadminUserOptionsQueryKey: ['superadmin', 'users', 'options'],
}));

const organizationA = {
  id: '00000000-0000-0000-0000-000000000101',
  name: 'Alpha VVS',
  filials: [{
    id: '00000000-0000-0000-0000-000000000111',
    name: 'Alpha København',
    isDefault: true,
  }],
};

const organizationB = {
  id: '00000000-0000-0000-0000-000000000201',
  name: 'Beta El',
  filials: [{
    id: '00000000-0000-0000-0000-000000000211',
    name: 'Beta Aarhus',
    isDefault: true,
  }],
};

const user = {
  id: '00000000-0000-0000-0000-000000000301',
  organizationId: organizationB.id,
  organizationName: organizationB.name,
  filialId: organizationB.filials[0].id,
  filialName: organizationB.filials[0].name,
  email: 'employee@beta.test',
  displayName: 'Beta Employee',
  phone: '12345678',
  role: 'User',
  userKind: 'InternalTest',
  createdAt: '2026-08-09T20:00:00Z',
  updatedAt: '2026-08-09T20:00:00Z',
};

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return function Wrapper({ children }: PropsWithChildren) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}

describe('SuperAdminUsersPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getOptions.mockResolvedValue({
      organizations: [organizationA, organizationB],
      roles: ['User', 'Admin', 'Auditor'],
      userKinds: ['Member', 'InternalTest'],
    });
    api.getUsers.mockResolvedValue({ users: [user], total: 1 });
    api.updateUser.mockImplementation(async (_id, input) => ({ ...user, ...input }));
    api.createUser.mockResolvedValue(user);
    api.deleteUser.mockResolvedValue(undefined);
  });

  it('shows tenant, filial and internal-test context for cross-organization users', async () => {
    render(<SuperAdminUsersPanel />, { wrapper: createWrapper() });

    expect(await screen.findByText('Beta Employee')).toBeInTheDocument();
    expect(screen.getByText('employee@beta.test')).toBeInTheDocument();
    expect(screen.getByText('Beta El · Beta Aarhus')).toBeInTheDocument();
    expect(screen.getByText('Brugergruppe: Intern test')).toBeInTheDocument();
    expect(screen.getByText('1 bruger')).toBeInTheDocument();
  });

  it('updates role and user group through the existing superadmin edit flow', async () => {
    render(<SuperAdminUsersPanel />, { wrapper: createWrapper() });

    await screen.findByText('Beta Employee');
    fireEvent.click(screen.getByRole('button', { name: 'Redigér' }));

    expect(screen.getByDisplayValue('Beta El')).toBeDisabled();
    expect(screen.getByDisplayValue('employee@beta.test')).toBeDisabled();
    expect(screen.getByLabelText('Brugergruppe')).toHaveValue('InternalTest');

    fireEvent.change(screen.getByLabelText('Rolle'), { target: { value: 'Admin' } });
    fireEvent.change(screen.getByLabelText('Brugergruppe'), { target: { value: 'Member' } });
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændringer' }));

    await waitFor(() => expect(api.updateUser).toHaveBeenCalledWith(
      user.id,
      expect.objectContaining({
        role: 'Admin',
        filialId: organizationB.filials[0].id,
        userKind: 'Member',
      }),
    ));
  });

  it('defaults a new superadmin-created user to the customer audience', async () => {
    render(<SuperAdminUsersPanel />, { wrapper: createWrapper() });

    await screen.findByText('Beta Employee');
    fireEvent.click(screen.getByRole('button', { name: 'Ny bruger' }));

    expect(screen.getByLabelText('Brugergruppe')).toHaveValue('Member');
  });
});
