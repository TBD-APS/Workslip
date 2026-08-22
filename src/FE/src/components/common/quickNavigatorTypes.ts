import type { JobListItemViewModel } from '../../api/generated/models';
import type { CustomerSearchViewModel } from '../../api/generated/models';
import type { DocumentListItemResponse } from '../../api/generated/models';

export interface QuickNavigatorSearchResult {
  jobs: JobListItemViewModel[];
  customers: CustomerSearchViewModel[];
  documents: DocumentListItemResponse[];
  isLoading: boolean;
  isLoadingJobs: boolean;
  isLoadingCustomers: boolean;
  isLoadingDocuments: boolean;
  jobError: boolean;
  customerError: boolean;
  documentError: boolean;
}

export interface QuickNavigatorSearchScope {
  canSearchJobs: boolean;
  canViewAllJobs: boolean;
  currentUserId?: string;
  canViewCustomers: boolean;
  canViewDocs: boolean;
  query: string;
  isOpen: boolean;
}
