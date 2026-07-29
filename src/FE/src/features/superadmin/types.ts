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
