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

export interface JobLinkInfoResponse {
  linkedReportId: string;
  linkedReportNumber: string;
  linkedCustomerName: string;
  linkedStatus: string;
  linkType: string;
}

export interface JobWorkKindResponse {
  id: string;
  normalizedLabel: string;
  label: string;
  requiresCustomWorkKind: boolean;
  sortOrder: number | string;
  customWorkKind?: string | null;
}

export interface JobReportSummaryObservationResponse {
  reportDate?: string | null;
  taskDescription?: string | null;
  customerObservations?: string | null;
  technicalObservations?: string | null;
}

export interface JobReportSummaryWorkResponse {
  workKind?: JobWorkKindResponse | null;
  installationTypes: unknown[];
  closureFlags: unknown[];
  remarks?: string | null;
}

export interface JobReportSummaryViewModel {
  id: string;
  organizationId: string;
  reportNumber?: string | null;
  status: JobStatus;
  customer: CustomerViewModel;
  work: JobReportSummaryWorkResponse;
  observations: JobReportSummaryObservationResponse;
  links: JobLinkInfoResponse[];
  assignedUsers: AssignedUserResponse[];
  softDeleted: boolean;
}

export interface JobListItemViewModel {
  id: string;
  organizationId: string;
  customer?: CustomerViewModel;
  reportNumber?: string;
  status: JobStatus;
  reportDate?: string;
  installationTypes: string[];
  workKind?: JobWorkKindResponse;
  customWorkKind?: string;
  assignedUsers: AssignedUserResponse[];
  softDeleted: boolean;
  totalHours?: number;
}

export interface WorksheetDayViewModel {
  isSleepingOnJob : boolean;
  workDate: string;
  hoursWorked: number;
}
