import type { CustomerInfo } from '../../api/generated/models';

export type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

export type JobWorkForm = {
  categoryIds: string[];
  workKind: string;
  customWorkKind: string;
};

export type ReferenceCategory = {
  id: string;
  name: string;
  sortOrder: number | string;
  categories: Array<{
    id: string;
    controlPoints: Array<{
      id: string;
      sortOrder: number | string;
      isRequired: boolean;
    }>;
  }>;
};

export type ReferenceWorkKind = {
  normalizedLabel: string;
  label: string;
  requiresCustomWorkKind: boolean;
  sortOrder: number | string;
};

export type ReferenceData = {
  installationTypes: ReferenceCategory[];
  workKinds: ReferenceWorkKind[];
};

export type JobForm = {
  customer: CustomerInfo;
  reportNumber: string;
  taskDescription: string;
  customerObservations: string;
  work: JobWorkForm;
};

export type AssignableUser = {
  id: string;
  displayName: string;
  email: string;
};

export type LinkableJob = {
  id: string;
  label: string;
  description: string;
};
