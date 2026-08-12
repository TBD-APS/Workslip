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

export interface SuperAdminUser {
  id: string;
  organizationId: string;
  organizationName: string;
  filialId: string;
  filialName: string;
  email: string;
  displayName: string;
  phone: string;
  role: string;
  userKind: string;
  createdAt: string;
  updatedAt: string;
}

export interface SuperAdminUserList {
  users: SuperAdminUser[];
  total: number;
}

export interface SuperAdminFilialOption {
  id: string;
  name: string;
  isDefault: boolean;
}

export interface SuperAdminOrganizationOption {
  id: string;
  name: string;
  filials: SuperAdminFilialOption[];
}

export interface SuperAdminUserOptions {
  organizations: SuperAdminOrganizationOption[];
  roles: string[];
  userKinds: string[];
}

export interface CreateSuperAdminUserInput {
  organizationId: string;
  filialId: string;
  email: string;
  displayName: string;
  phone: string;
  role: string;
  userKind?: string;
}

export interface UpdateSuperAdminUserInput {
  displayName?: string;
  phone?: string;
  role?: string;
  filialId?: string;
  userKind?: string;
}
