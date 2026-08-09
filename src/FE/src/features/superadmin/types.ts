export interface Organization {
  id: string;
  name: string;
  cvr: string;
}

export interface OrganizationAdmin {
  id: string;
  organizationId: string;
  displayName: string;
  email: string | null;
  phone: string | null;
  role: string;
  entraInvitationSent: boolean;
}

export interface OrganizationOnboarding {
  organization: Organization;
  user: OrganizationAdmin;
}

export interface OrganizationSessionToken {
  token: string;
  tokenType: string;
  expiresIn: number;
  user: {
    userId: string;
    organizationId: string;
    email: string;
    displayName: string;
    role: string;
  };
}

export interface CreateOrganizationInput {
  name: string;
  cvr: string;
  adminDisplayName: string;
}

export interface InviteOrganizationAdminInput {
  organizationId: string;
  email: string;
  displayName: string;
  phone: string;
}

export interface AdminUser {
  id: string;
  organizationId: string;
  organizationName: string;
  email: string;
  displayName: string;
  phone: string;
  role: string;
  roleDisplayName: string;
}

export interface AdminUserListResponse {
  users: AdminUser[];
  total: number;
}

export interface CreateAdminUserInput {
  organizationId: string;
  email: string;
  displayName: string;
  phone: string;
  role: string;
}

export interface UpdateAdminUserInput {
  displayName?: string;
  phone?: string;
  role?: string;
}
