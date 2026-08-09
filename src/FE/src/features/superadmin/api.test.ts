import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../lib/axios';
import {
  createAdminUser,
  createOrganization,
  createOrganizationSession,
  deleteAdminUser,
  getAdminUsers,
  getOrganizations,
  getSuperadminErrorMessage,
  inviteOrganizationAdmin,
  updateAdminUser,
} from './api';

vi.mock('../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}));

describe('Superadmin API', () => {
  beforeEach(() => vi.clearAllMocks());

  it('uses all organization endpoints from a mobile browser', async () => {
    vi.stubGlobal('navigator', {
      userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9)',
      maxTouchPoints: 5,
    });

    const onboarding = {
      organization: {
        id: 'organization-id',
        name: 'Organisation',
        cvr: '12345678',
      },
      user: {
        id: 'user-id',
        organizationId: 'organization-id',
        displayName: 'Administrator',
        email: null,
        phone: null,
        role: 'Admin',
        entraInvitationSent: false,
      },
    };
    const session = {
      token: 'delegated-token',
      tokenType: 'Bearer',
      expiresIn: 900,
      user: {
        userId: 'user-id',
        organizationId: 'organization-id',
        email: 'superadmin@example.com',
        displayName: 'Super Admin',
        role: 'Superadmin',
      },
    };
    const admin = {
      id: 'admin-id',
      organizationId: 'organization-id',
      displayName: 'Administrator',
      email: 'admin@example.com',
      phone: null,
      role: 'Admin',
      entraInvitationSent: true,
    };

    vi.mocked(apiClient.get).mockResolvedValue([]);
    vi.mocked(apiClient.post)
      .mockResolvedValueOnce(onboarding)
      .mockResolvedValueOnce(session);
    vi.mocked(apiClient.put).mockResolvedValue(admin);

    await expect(getOrganizations()).resolves.toEqual([]);
    await expect(createOrganization({
      name: ' Organisation ',
      cvr: ' 12345678 ',
      adminDisplayName: ' Administrator ',
    })).resolves.toEqual(onboarding);
    await expect(createOrganizationSession('organization-id')).resolves.toEqual(session);
    await expect(inviteOrganizationAdmin({
      organizationId: 'organization-id',
      email: ' admin@example.com ',
      displayName: ' Administrator ',
      phone: '',
    })).resolves.toEqual(admin);

    expect(apiClient.get).toHaveBeenCalledWith('/api/organizations', {
      skipGlobalErrorToast: true,
    });
    expect(apiClient.post).toHaveBeenNthCalledWith(1, '/api/organizations', {
      name: 'Organisation',
      cvr: '12345678',
      adminDisplayName: 'Administrator',
      adminEmail: null,
      adminPhone: null,
    }, {
      skipGlobalErrorToast: true,
    });
    expect(apiClient.post).toHaveBeenNthCalledWith(
      2,
      '/api/organizations/organization-id/session',
      undefined,
      { skipGlobalErrorToast: true },
    );
    expect(apiClient.put).toHaveBeenCalledWith(
      '/api/organizations/organization-id/admin',
      {
        email: 'admin@example.com',
        displayName: 'Administrator',
        phone: null,
      },
      { skipGlobalErrorToast: true },
    );
  });

  it('uses the cross-org admin user endpoints', async () => {
    const user = {
      id: 'user-id',
      organizationId: 'organization-id',
      organizationName: 'Kunde A/S',
      email: 'user@example.com',
      displayName: 'Bruger',
      phone: '',
      role: 'User',
      roleDisplayName: 'Medarbejder',
    };
    const list = { users: [user], total: 1 };

    vi.mocked(apiClient.get).mockResolvedValue(list);
    vi.mocked(apiClient.post).mockResolvedValue(user);
    vi.mocked(apiClient.patch).mockResolvedValue({ ...user, role: 'Admin' });
    vi.mocked(apiClient.delete).mockResolvedValue(undefined);

    await expect(getAdminUsers({ organizationId: 'organization-id', limit: 20, offset: 0 }))
      .resolves.toEqual(list);
    await expect(createAdminUser({
      organizationId: 'organization-id',
      email: ' new@example.com ',
      displayName: ' New User ',
      phone: '',
      role: 'User',
    })).resolves.toEqual(user);
    await expect(updateAdminUser('user-id', { role: 'Admin' }))
      .resolves.toEqual({ ...user, role: 'Admin' });
    await expect(deleteAdminUser('user-id')).resolves.toBeUndefined();

    expect(apiClient.get).toHaveBeenCalledWith('/api/superadmin/users', {
      params: { organizationId: 'organization-id', limit: 20, offset: 0 },
      skipGlobalErrorToast: true,
    });
    expect(apiClient.post).toHaveBeenCalledWith('/api/superadmin/users', {
      organizationId: 'organization-id',
      email: 'new@example.com',
      displayName: 'New User',
      phone: null,
      role: 'User',
    }, { skipGlobalErrorToast: true });
    expect(apiClient.patch).toHaveBeenCalledWith('/api/superadmin/users/user-id', {
      displayName: null,
      phone: null,
      role: 'Admin',
    }, { skipGlobalErrorToast: true });
    expect(apiClient.delete).toHaveBeenCalledWith('/api/superadmin/users/user-id', {
      skipGlobalErrorToast: true,
    });
  });

  it('maps self-action and stale-state conflicts to Danish messages', () => {
    const selfActionError = {
      isAxiosError: true,
      response: { status: 409, data: { error: 'self_action_not_allowed' } },
    };
    const staleStateError = {
      isAxiosError: true,
      response: { status: 409, data: { error: 'user_state_changed' } },
    };

    expect(getSuperadminErrorMessage(selfActionError))
      .toBe('Du kan ikke ændre rolle eller slette din egen Superadmin-konto herfra.');
    expect(getSuperadminErrorMessage(staleStateError))
      .toBe('Brugeren blev ændret samtidig. Genindlæs listen og prøv igen.');
  });
});
