export type JobStatus = 'Draft' | 'Assigned' | 'Submitted' | 'InReview' | 'Approved' | 'Rejected' | 'Returned' | 'Archived';

export interface CustomerViewModel {
  customerId?: string;
  name?: string;
  address?: string;
  email?: string;
  contactPerson?: string;
  phone?: string;
}

export interface AssignedUserResponse {
  userId: string;
  displayName: string;
}

export interface JobListItemViewModel {
  id: string;
  organizationId: string;
  customer?: CustomerViewModel;
  reportNumber?: string;
  status: JobStatus;
  reportDate?: string;
  installationTypes: string[];
  workKind?: string;
  customWorkKind?: string;
  assignedUsers: AssignedUserResponse[];
  softDeleted: boolean;
  totalHours?: number;
}
