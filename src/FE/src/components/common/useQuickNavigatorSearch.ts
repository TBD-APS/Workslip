import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../../lib/axios';
import {
  getApiCustomersSearch,
  getGetApiCustomersSearchQueryKey,
} from '../../api/generated/customers/customers';
import type { JobListItemViewModel } from '../../api/generated/models';
import type { CustomerSearchViewModel } from '../../api/generated/models';
import {
  getQuickJobSearchTerm,
  getCustomerSearchTerm,
  filterQuickNavigationJobs,
} from './quickNavigatorSearch';
import type { QuickNavigatorSearchResult, QuickNavigatorSearchScope } from './quickNavigatorTypes';

type JobSearchResponse = {
  items: JobListItemViewModel[];
  totalCount: number;
};

async function fetchJobs(search: string, signal: AbortSignal): Promise<JobListItemViewModel[]> {
  const response = await apiClient.get('/api/jobs', {
    params: { search, limit: 5, offset: 0 },
    signal,
  });
  return (response.data as JobSearchResponse).items ?? [];
}

export function useQuickNavigatorSearch(scope: QuickNavigatorSearchScope): QuickNavigatorSearchResult {
  const {
    canSearchJobs,
    canViewAllJobs,
    currentUserId,
    canViewCustomers,
    query,
    isOpen,
  } = scope;

  const jobSearchTerm = getQuickJobSearchTerm(query);
  const customerSearchTerm = getCustomerSearchTerm(query);

  const jobsEnabled = isOpen && canSearchJobs && jobSearchTerm !== null;
  const customersEnabled = isOpen && canViewCustomers && customerSearchTerm !== null;

  const jobsQuery = useQuery<JobListItemViewModel[], Error>({
    queryKey: ['quick-navigator', 'jobs', jobSearchTerm ?? '', canViewAllJobs, currentUserId ?? ''],
    queryFn: ({ signal }) => fetchJobs(jobSearchTerm!, signal),
    enabled: jobsEnabled,
    staleTime: 30_000,
    retry: 1,
  });

  const customersQuery = useQuery<CustomerSearchViewModel[], Error>({
    queryKey: getGetApiCustomersSearchQueryKey({ query: customerSearchTerm ?? '', limit: 5 }),
    queryFn: ({ signal }) =>
      getApiCustomersSearch({ query: customerSearchTerm!, limit: 5 }, undefined, signal),
    enabled: customersEnabled,
    staleTime: 30_000,
    retry: 1,
  });

  const filteredJobs = useMemo(() => {
    const raw = jobsQuery.data ?? [];
    return filterQuickNavigationJobs(raw, canViewAllJobs, currentUserId);
  }, [jobsQuery.data, canViewAllJobs, currentUserId]);

  const isLoadingJobs = jobsEnabled && jobsQuery.isLoading;
  const isLoadingCustomers = customersEnabled && customersQuery.isLoading;

  const jobError = jobsQuery.isError && !jobsQuery.isLoading;
  const customerError = customersQuery.isError && !customersQuery.isLoading;

  return {
    jobs: filteredJobs,
    customers: customersQuery.data ?? [],
    isLoading: isLoadingJobs || isLoadingCustomers,
    isLoadingJobs,
    isLoadingCustomers,
    jobError,
    customerError,
  };
}
