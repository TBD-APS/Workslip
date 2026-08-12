import axios from 'axios';
import { apiClient } from '../../lib/axios';
import type {
  CreateOrganizationInput,
  CreateSuperAdminUserInput,
  InviteOrganizationAdminInput,
  Organization,
  OrganizationAdmin,
  OrganizationOnboarding,
  OrganizationSessionToken,
  SuperAdminUser,
  SuperAdminUserList,
  SuperAdminUserOptions,
  UpdateSuperAdminUserInput,
} from './types';

export const superadminOrganizationQueryKey = ['superadmin', 'organizations'] as const;
export const superadminUserQueryKey = ['superadmin', 'users'] as const;
export const superadminUserOptionsQueryKey = ['superadmin', 'users', 'options'] as const;
const organizationsPath = '/api/organizations';
const usersPath = '/api/superadmin/users';

export async function getOrganizations(): Promise<Organization[]> {
  return await apiClient.get(organizationsPath, {
    skipGlobalErrorToast: true,
  }) as unknown as Organization[];
}

export async function createOrganization(input: CreateOrganizationInput): Promise<OrganizationOnboarding> {
  return await apiClient.post(organizationsPath, {
    name: input.name.trim(),
    cvr: input.cvr.trim(),
    adminDisplayName: input.adminDisplayName.trim(),
    adminEmail: null,
    adminPhone: null,
  }, {
    skipGlobalErrorToast: true,
  }) as unknown as OrganizationOnboarding;
}

export async function createOrganizationSession(organizationId: string): Promise<OrganizationSessionToken> {
  return await apiClient.post(`/api/organizations/${organizationId}/session`, undefined, {
    skipGlobalErrorToast: true,
  }) as unknown as OrganizationSessionToken;
}

export async function inviteOrganizationAdmin(input: InviteOrganizationAdminInput): Promise<OrganizationAdmin> {
  return await apiClient.put(`/api/organizations/${input.organizationId}/admin`, {
    email: input.email.trim(),
    displayName: input.displayName.trim(),
    phone: input.phone.trim() || null,
  }, {
    skipGlobalErrorToast: true,
  }) as unknown as OrganizationAdmin;
}

export async function getSuperadminUsers(input: {
  limit: number;
  offset: number;
  search?: string;
}): Promise<SuperAdminUserList> {
  return await apiClient.get(usersPath, {
    params: {
      limit: input.limit,
      offset: input.offset,
      search: input.search?.trim() || undefined,
      sortBy: 'organization',
      sortDirection: 'asc',
    },
    skipGlobalErrorToast: true,
  }) as unknown as SuperAdminUserList;
}

export async function getSuperadminUserOptions(): Promise<SuperAdminUserOptions> {
  return await apiClient.get(`${usersPath}/options`, {
    skipGlobalErrorToast: true,
  }) as unknown as SuperAdminUserOptions;
}

export async function createSuperadminUser(input: CreateSuperAdminUserInput): Promise<SuperAdminUser> {
  return await apiClient.post(usersPath, {
    organizationId: input.organizationId,
    filialId: input.filialId,
    email: input.email.trim(),
    displayName: input.displayName.trim(),
    phone: input.phone.trim(),
    role: input.role,
  }, {
    skipGlobalErrorToast: true,
  }) as unknown as SuperAdminUser;
}

export async function updateSuperadminUser(
  userId: string,
  input: UpdateSuperAdminUserInput,
): Promise<SuperAdminUser> {
  return await apiClient.patch(`${usersPath}/${userId}`, input, {
    skipGlobalErrorToast: true,
  }) as unknown as SuperAdminUser;
}

export async function deleteSuperadminUser(userId: string): Promise<void> {
  await apiClient.delete(`${usersPath}/${userId}`, {
    skipGlobalErrorToast: true,
  });
}

export function getSuperadminErrorMessage(error: unknown): string {
  if (!axios.isAxiosError(error)) {
    return 'Der opstod en uventet fejl.';
  }

  const status = error.response?.status;
  const data = error.response?.data as {
    error?: string;
    message?: string;
    errors?: Record<string, string[]>;
  } | undefined;

  if (status === 403) {
    return 'Du har ikke Superadmin-adgang til denne handling.';
  }

  if (status === 404) {
    return 'Elementet findes ikke længere. Genindlæs og prøv igen.';
  }

  if (status === 409) {
    switch (data?.error ?? data?.message) {
      case 'organization_cvr_exists':
        return 'Der findes allerede en organisation med dette CVR-nummer.';
      case 'email_in_use':
        return 'E-mailadressen tilhører allerede en Workslip-bruger.';
      case 'superadmin_role_protected':
        return 'En Superadmin-konto kan ikke ændres til almindelig administrator.';
      case 'admin_state_changed':
        return 'Administratoren blev ændret samtidig. Genindlæs organisationerne og prøv igen.';
      case 'user_has_history':
        return 'Brugeren har historik på sager eller timesedler og kan derfor ikke slettes.';
      default:
        return 'Handlingen kunne ikke gennemføres på grund af en konflikt.';
    }
  }

  const validationMessage = data?.errors
    ? Object.values(data.errors).flat().find(Boolean)
    : undefined;

  return validationMessage
    ?? data?.message
    ?? error.message
    ?? 'Der opstod en uventet fejl.';
}
