import type { CustomerInfo } from '../../api/generated/models';

export type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

export type JobForm = {
  customer: CustomerInfo;
  reportNumber: string;
  taskDescription: string;
  customerObservations: string;
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
