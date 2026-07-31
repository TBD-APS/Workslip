import axios from 'axios';
import { apiClient } from '../../lib/axios';
import type {
  CreateOrganizationInput,
  InviteOrganizationAdminInput,
  Organization,
  OrganizationAdmin,
  OrganizationOnboarding,
  OrganizationSessionToken,
} from './types';

export const superadminOrganizationQueryKey = ['superadmin', 'organizations'] as const;
const organizationsPath = '/api/organizations';

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
    return 'Organisationen findes ikke længere. Genindlæs listen og prøv igen.';
  }

  if (status === 409) {
    switch (data?.error ?? data?.message) {
      case 'organization_cvr_exists':
        return 'Der findes allerede en organisation med dette CVR-nummer.';
      case 'email_in_use':
        return 'E-mailadressen tilhører allerede en bruger i en anden organisation.';
      case 'superadmin_role_protected':
        return 'En Superadmin-konto kan ikke ændres til almindelig administrator.';
      case 'admin_state_changed':
        return 'Administratoren blev ændret samtidig. Genindlæs organisationerne og prøv igen.';
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
