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

export interface WorksheetDayViewModel {
  workDate: string;
  hoursWorked: number;
}

export interface WorksheetUserGroupViewModel {
  displayName: string;
  totalHours: number;
  entries: WorksheetDayViewModel[];
}

export interface ControlCheckResponse {
  id: string;
  itemId: string;
  checked: boolean;
  note?: string;
}

export interface ControlSubcategoryResponse {
  id: string;
  installationTypeId: string;
  subcategoryId: string;
  controlChecks: ControlCheckResponse[];
}

export interface ControlInstallationTypeViewModel {
  installationTypeId: string;
  subcategories: ControlSubcategoryResponse[];
}

export interface JobViewModel {
  id: string;
  organizationId: string;
  customer?: CustomerViewModel;
  reportNumber?: string;
  status: JobStatus;
  reportDate?: string;
  taskDescription?: string;
  customerObservations?: string;
  technicalObservations?: string;
  installationTypes: string[];
  workKind?: string;
  customWorkKind?: string;
  remarks?: string;
  closureFlags: string[];
  controlInstallationTypes: ControlInstallationTypeViewModel[];
  links: JobLinkInfoResponse[];
  assignedUsers: AssignedUserResponse[];
  worksheets: WorksheetUserGroupViewModel[];
  softDeleted: boolean;
  totalHours?: number;
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
