import type { JobListItemViewModel } from '../../api/generated/models';
import type { CustomerSearchViewModel } from '../../api/generated/models';

export interface QuickNavigatorSearchResult {
  jobs: JobListItemViewModel[];
  customers: CustomerSearchViewModel[];
  isLoading: boolean;
  isLoadingJobs: boolean;
  isLoadingCustomers: boolean;
  jobError: boolean;
  customerError: boolean;
  jobSearchDegraded: boolean;
  customerSearchDegraded: boolean;
}

export interface QuickNavigatorSearchScope {
  canSearchJobs: boolean;
  canViewAllJobs: boolean;
  currentUserId?: string;
  canViewCustomers: boolean;
  query: string;
  isOpen: boolean;
}
