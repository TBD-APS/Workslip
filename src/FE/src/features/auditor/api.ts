import type { AxiosResponse } from 'axios';
import { apiClient } from '../../lib/axios';

// Auditor access is intentionally routed through dedicated server-authorized endpoints.
// Do not reuse ordinary tenant job APIs here: cross-tenant reads must remain assignment-gated.

export type AuditorOrganizationSummary = {
  assignmentId: string;
  organizationId: string;
  organizationName: string;
  cvr: string;
  authorizationArea: string;
  activeFrom: string;
  activeUntil: string | null;
  openFindings: number;
  lastActivityAt: string | null;
};

export type AuditorReportListItem = {
  id: string;
  reportNumber: string | null;
  customerName: string | null;
  address: string | null;
  status: string;
  installationTypes: string | null;
  totalHours: number | null;
  assignedUsers: string | null;
  submittedBy: string | null;
  reportDate: string | null;
  submittedAt: string | null;
  updatedAt: string;
  openFindings: number;
};

export type AuditorReportListResponse = {
  items: AuditorReportListItem[];
  totalCount: number;
};

export type AuditorControlPoint = {
  id: string;
  name: string;
  isRequired: boolean;
  isChecked: boolean;
};

export type AuditorControlCategory = {
  id: string;
  name: string;
  isIrrelevant: boolean;
  controlPoints: AuditorControlPoint[];
};

export type AuditorInstallation = {
  id: string;
  name: string;
  categories: AuditorControlCategory[];
};

export type AuditorPerson = {
  id: string;
  displayName: string;
};

export type AuditorWorksheet = {
  id: string;
  workDate: string;
  hoursWorked: number;
  userId: string;
  userName: string;
};

export type AuditorFinding = {
  id: string;
  assignmentId: string;
  category: 'A' | 'An' | 'Anb' | 'IR';
  description: string;
  reference: string | null;
  dueAt: string | null;
  status: 'Open' | 'AwaitingEvidence' | 'ReadyForVerification' | 'Closed';
  companyEvidence: string | null;
  companyEvidenceAt: string | null;
  createdBy: string | null;
  companyEvidenceBy: string | null;
  verifiedBy: string | null;
  verifiedAt: string | null;
  createdAt: string;
  updatedAt: string;
};

export type AuditorEvent = {
  id: string;
  eventType: string;
  createdAt: string;
  actorName: string | null;
};

export type AuditorImageInfo = {
  id: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
};

export type AuditorReportDetail = {
  id: string;
  organizationName: string;
  organizationCvr: string;
  reportNumber: string | null;
  customerName: string | null;
  customerAddress: string | null;
  destinationAddress: string | null;
  destinationZipCode: string | null;
  destinationCity: string | null;
  status: string;
  jobType: string;
  reportDate: string | null;
  taskDescription: string | null;
  customerObservations: string | null;
  technicalObservations: string | null;
  remarks: string | null;
  createdAt: string;
  updatedAt: string;
  submittedAt: string | null;
  submittedBy: string | null;
  installations: AuditorInstallation[];
  assignedUsers: AuditorPerson[];
  worksheets: AuditorWorksheet[];
  findings: AuditorFinding[];
  auditEvents: AuditorEvent[];
  images: AuditorImageInfo[];
};

export type CreateAuditorFindingRequest = {
  category: AuditorFinding['category'];
  description: string;
  reference: string | null;
  dueAt: string | null;
};

export type UpdateAuditorFindingRequest = {
  status: AuditorFinding['status'];
  description: string | null;
  dueAt: string | null;
};

export async function getAuditorOrganizations(): Promise<AuditorOrganizationSummary[]> {
  return apiClient.get<unknown, AuditorOrganizationSummary[]>('/api/auditor/organizations');
}

export async function getAuditorReports(
  organizationId: string,
  options: { search?: string; installationType?: string; limit?: number; offset?: number } = {},
): Promise<AuditorReportListResponse> {
  return apiClient.get<unknown, AuditorReportListResponse>(`/api/auditor/organizations/${organizationId}/reports`, { params: options });
}

export async function getAuditorReport(organizationId: string, jobId: string): Promise<AuditorReportDetail> {
  return apiClient.get<unknown, AuditorReportDetail>(`/api/auditor/organizations/${organizationId}/reports/${jobId}`);
}

export async function createAuditorFinding(
  organizationId: string,
  jobId: string,
  request: CreateAuditorFindingRequest,
): Promise<AuditorFinding> {
  return apiClient.post<unknown, AuditorFinding>(`/api/auditor/organizations/${organizationId}/reports/${jobId}/findings`, request);
}

export async function updateAuditorFinding(
  organizationId: string,
  jobId: string,
  findingId: string,
  request: UpdateAuditorFindingRequest,
): Promise<AuditorFinding> {
  return apiClient.patch<unknown, AuditorFinding>(`/api/auditor/organizations/${organizationId}/reports/${jobId}/findings/${findingId}`, request);
}

export async function getAuditorImageBlob(
  organizationId: string,
  jobId: string,
  imageId: string,
): Promise<Blob> {
  const response = await apiClient.get<unknown, AxiosResponse<Blob>>(
    `/api/auditor/organizations/${organizationId}/reports/${jobId}/images/${imageId}`,
    { responseType: 'blob' },
  );
  return response.data;
}
