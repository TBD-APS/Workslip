import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../../lib/axios';
import {
  getApiCustomersSearch,
  getGetApiCustomersSearchQueryKey,
} from '../../api/generated/customers/customers';
import type { JobListItemViewModel } from '../../api/generated/models';
import type { CustomerSearchViewModel } from '../../api/generated/models';
import { useDebounce } from '../../hooks/useDebounce';
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

  const debouncedQuery = useDebounce(query, 200);

  const jobSearchTerm = getQuickJobSearchTerm(debouncedQuery);
  const customerSearchTerm = getCustomerSearchTerm(debouncedQuery);

  const rawJobSearchTerm = getQuickJobSearchTerm(query);
  const rawCustomerSearchTerm = getCustomerSearchTerm(query);

  const hasRemoteJobIntent = rawJobSearchTerm !== null;
  const hasRemoteCustomerIntent = rawCustomerSearchTerm !== null;

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

  const isDebouncing = query !== debouncedQuery;

  const filteredJobs = useMemo(() => {
    const raw = jobsQuery.data ?? [];
    return filterQuickNavigationJobs(raw, canViewAllJobs, currentUserId);
  }, [jobsQuery.data, canViewAllJobs, currentUserId]);

  const isLoadingJobs = isOpen && canSearchJobs && hasRemoteJobIntent && (isDebouncing || jobsQuery.isLoading);
  const isLoadingCustomers = isOpen && canViewCustomers && hasRemoteCustomerIntent && (isDebouncing || customersQuery.isLoading);

  const jobError = !isDebouncing && jobsQuery.isError && !jobsQuery.isLoading;
  const customerError = !isDebouncing && customersQuery.isError && !customersQuery.isLoading;

  return {
    jobs: isDebouncing ? [] : filteredJobs,
    customers: isDebouncing ? [] : (customersQuery.data ?? []),
    isLoading: isLoadingJobs || isLoadingCustomers,
    isLoadingJobs,
    isLoadingCustomers,
    jobError,
    customerError,
  };
}
