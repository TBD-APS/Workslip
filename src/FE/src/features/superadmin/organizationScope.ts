export const ORGANIZATION_SCOPE_HEADER = 'X-Workslip-Organization-Id';

const ORGANIZATION_SCOPE_ID_KEY = 'workslip.superadmin.organizationId';
const ORGANIZATION_SCOPE_NAME_KEY = 'workslip.superadmin.organizationName';

export interface OrganizationScope {
  id: string;
  name: string;
}

export function getOrganizationScope(): OrganizationScope | null {
  const id = localStorage.getItem(ORGANIZATION_SCOPE_ID_KEY)?.trim();
  if (!id) return null;

  return {
    id,
    name: localStorage.getItem(ORGANIZATION_SCOPE_NAME_KEY)?.trim() || 'Valgt organisation',
  };
}

export function setOrganizationScope(scope: OrganizationScope): void {
  localStorage.setItem(ORGANIZATION_SCOPE_ID_KEY, scope.id);
  localStorage.setItem(ORGANIZATION_SCOPE_NAME_KEY, scope.name);
}

export function clearOrganizationScope(): void {
  localStorage.removeItem(ORGANIZATION_SCOPE_ID_KEY);
  localStorage.removeItem(ORGANIZATION_SCOPE_NAME_KEY);
}
