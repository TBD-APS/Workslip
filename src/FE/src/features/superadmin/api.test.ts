import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../lib/axios';
import {
  createOrganization,
  createOrganizationSession,
  getOrganizations,
  inviteOrganizationAdmin,
} from './api';

vi.mock('../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}));

function useDevice(device: 'mobile' | 'desktop'): void {
  vi.stubGlobal('navigator', device === 'mobile'
    ? {
      userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9)',
      maxTouchPoints: 5,
    }
    : {
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
      maxTouchPoints: 0,
    });
}

describe('Superadmin API device defense', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.unstubAllGlobals();
  });

  it('issues zero organization requests on mobile', async () => {
    useDevice('mobile');

    await expect(getOrganizations()).rejects.toThrow(
      'Superadmin er kun tilgængelig på computer.',
    );
    await expect(createOrganization({
      name: 'Organisation',
      cvr: '12345678',
      adminDisplayName: 'Administrator',
    })).rejects.toThrow('Superadmin er kun tilgængelig på computer.');
    await expect(createOrganizationSession('organization-id')).rejects.toThrow(
      'Superadmin er kun tilgængelig på computer.',
    );
    await expect(inviteOrganizationAdmin({
      organizationId: 'organization-id',
      email: 'admin@example.com',
      displayName: 'Administrator',
      phone: '',
    })).rejects.toThrow('Superadmin er kun tilgængelig på computer.');

    expect(apiClient.get).not.toHaveBeenCalled();
    expect(apiClient.post).not.toHaveBeenCalled();
    expect(apiClient.put).not.toHaveBeenCalled();
  });

  it('uses the Vercel-compatible organization list path on desktop', async () => {
    useDevice('desktop');
    vi.mocked(apiClient.get).mockResolvedValue([]);

    await expect(getOrganizations()).resolves.toEqual([]);
    expect(apiClient.get).toHaveBeenCalledOnce();
    expect(apiClient.get).toHaveBeenCalledWith('/api/organizations', {
      skipGlobalErrorToast: true,
    });
  });

  it('uses the Vercel-compatible organization create path on desktop', async () => {
    useDevice('desktop');
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
    vi.mocked(apiClient.post).mockResolvedValue(onboarding);

    await expect(createOrganization({
      name: ' Organisation ',
      cvr: ' 12345678 ',
      adminDisplayName: ' Administrator ',
    })).resolves.toEqual(onboarding);

    expect(apiClient.post).toHaveBeenCalledOnce();
    expect(apiClient.post).toHaveBeenCalledWith('/api/organizations', {
      name: 'Organisation',
      cvr: '12345678',
      adminDisplayName: 'Administrator',
      adminEmail: null,
      adminPhone: null,
    }, {
      skipGlobalErrorToast: true,
    });
  });
});
