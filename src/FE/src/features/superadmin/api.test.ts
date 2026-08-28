import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../lib/axios';
import {
  createOrganization,
  createOrganizationSession,
  getOrganizations,
  inviteOrganizationAdmin,
  updateOrganizationAccountingProvider,
} from './api';

vi.mock('../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}));

describe('Superadmin API', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls every organization endpoint with the expected URL, method and trimmed input', async () => {
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

  it('updates accounting provider with trimmed or null provider ID', async () => {
    vi.mocked(apiClient.put).mockResolvedValue(undefined);

    await updateOrganizationAccountingProvider({
      organizationId: 'organization-1',
      providerId: ' economics ',
    });

    expect(apiClient.put).toHaveBeenCalledWith(
      '/api/organizations/organization-1/accounting-provider',
      { providerId: 'economics', agreementGrantToken: null, appSecretToken: null },
      { skipGlobalErrorToast: true },
    );

    await updateOrganizationAccountingProvider({
      organizationId: 'organization-2',
      providerId: '',
    });

    expect(apiClient.put).toHaveBeenCalledWith(
      '/api/organizations/organization-2/accounting-provider',
      { providerId: null, agreementGrantToken: null, appSecretToken: null },
      { skipGlobalErrorToast: true },
    );
  });

  it('sends economics tokens when provider is economics', async () => {
    vi.mocked(apiClient.put).mockResolvedValue(undefined);

    await updateOrganizationAccountingProvider({
      organizationId: 'org-1',
      providerId: 'economics',
      agreementGrantToken: ' grant-123 ',
      appSecretToken: ' secret-456 ',
    });

    expect(apiClient.put).toHaveBeenCalledWith(
      '/api/organizations/org-1/accounting-provider',
      { providerId: 'economics', agreementGrantToken: 'grant-123', appSecretToken: 'secret-456' },
      { skipGlobalErrorToast: true },
    );
  });
});
