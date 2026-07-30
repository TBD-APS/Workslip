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

  it('keeps the desktop organization request path available', async () => {
    useDevice('desktop');
    vi.mocked(apiClient.get).mockResolvedValue([]);

    await expect(getOrganizations()).resolves.toEqual([]);
    expect(apiClient.get).toHaveBeenCalledOnce();
  });
});
