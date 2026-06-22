import { useCallback, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AlertCircle, Building2, ChevronRight, Mail, Users, MapPin, MoreHorizontal, Phone } from 'lucide-react';
import { type CustomerListItemViewModel } from '../../../api/generated/models';
import { SearchBar } from '../../../components/filters/SearchBar';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { useInfiniteList } from '../../../hooks/useInfiniteList';
import { useInfiniteScroll } from '../../../hooks/useInfiniteScroll';
import { useSearch } from '../../../hooks/useSearch';
import { apiClient } from '../../../lib/axios';
import { useCustomerActions } from '../components/CustomerActions';

const PAGE_SIZE = 20;

const SkeletonCard = () => (
  <div className="job-card job-card-skeleton" aria-hidden="true">
    <div className="job-card-header">
      <div className="skeleton skeleton-name skeleton-w-60" />
    </div>
    <div className="skeleton skeleton-address skeleton-w-40" />
    <div className="skeleton skeleton-tag skeleton-w-30" />
  </div>
);

export const CustomerList = () => {
  const navigate = useNavigate();
  const [search, setSearch] = useState('');

  const fetchCustomersPage = useCallback(
    async ({ limit, offset }: { limit: number; offset: number }) =>
      (await apiClient.get('/api/customers', {
        params: { limit, offset },
      })) as CustomerListItemViewModel[],
    [],
  );

  const query = useInfiniteList({
    queryKey: ['/api/customers', { limit: PAGE_SIZE }],
    fetchPage: fetchCustomersPage,
    pageSize: PAGE_SIZE,
  });

  const { sentinelRef } = useInfiniteScroll({
    onReachEnd: () => {
      if (query.hasNextPage && !query.isFetchingNextPage && !query.isLoading) {
        void query.fetchNextPage();
      }
    },
    enabled: Boolean(query.hasNextPage) && !query.isFetchingNextPage && !query.isLoading,
  });

  const customers = useSearch(query.items, search, (customer, term) =>
    [customer.name, customer.address, customer.email, customer.contactPerson, customer.phone].some((value) => value?.toLowerCase().includes(term)),
  );

  const {
    toggleActionMenu,
    openActionMenu,
    ActionMenuPortal,
    EditDialog,
    DeleteDialog,
  } = useCustomerActions({ customers });

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="page-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
        <div className="job-list">
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
        </div>
      </div>
    );
  }

  if (query.isError) {
    return (
      <div className="page-container">
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente kunder. Prøv igen.</p>
          <button className="btn btn-primary" onClick={() => void query.refetch()}>
            Prøv igen
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="page-container">
      <div className="page-header">
        <h2>Kunder</h2>
        <p className="subtitle">{customers.length} {customers.length === 1 ? 'kunde' : 'kunder'}</p>
      </div>

      <SearchBar value={search} onChange={setSearch} placeholder="Søg kunder..." />
      <div className="search-bar-spacer" />

      <div className="job-list">
        {customers.map((customer) => (
          <div key={customer.id} className="job-card-wrapper">
            <button
              className="job-card"
              onClick={() => navigate(`/app/customers/${customer.id}`)}
              type="button"
            >
              <div className="job-card-top job-card-top-center">
                <Building2 size={20} className="customer-icon" />
                <h3 className="customer-name">{customer.name}</h3>
                <span className="meta-item customer-job-count">
                   {customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}
                </span>
              </div>

              <div className="job-card-body">
                {customer.address && (
                  <span className="meta-item">
                    <MapPin size={14} />
                    <span>{customer.address}</span>
                  </span>
                )}
                {customer.email && (
                  <span className="meta-item">
                    <Mail size={14} />
                    <span>{customer.email}</span>
                  </span>
                )}
                {customer.contactPerson && (
                  <span className="meta-item">
                    <Users size={14} />
                    <span>{customer.contactPerson}</span>
                  </span>
                )}
                {customer.phone && (
                  <span className="meta-item">
                    <Phone size={14} />
                    <span>{customer.phone}</span>
                  </span>
                )}
              </div>

              <div className="job-card-footer">
                <span />
                <span className="btn-icon" aria-label="Se kunde">
                  <ChevronRight size={20} />
                </span>
              </div>
            </button>

            <div className="worksheet-actions-menu-root customer-actions-anchor">
              <button
                type="button"
                className="btn-icon customer-actions-btn"
                onClick={(event) => {
                  event.stopPropagation();
                  toggleActionMenu(event, customer.id);
                }}
                aria-label="Åbn handlinger for kunde"
                aria-expanded={openActionMenu?.customerId === customer.id}
                title="Handlinger"
              >
                <MoreHorizontal size={18} />
              </button>
            </div>
          </div>
        ))}

        {customers.length === 0 && !query.isFetchingNextPage && (
          <div className="empty-state">
            <p>Ingen kunder fundet.</p>
          </div>
        )}

        <InfiniteScrollSentinel
          sentinelRef={sentinelRef}
          isLoading={query.isFetchingNextPage}
        />
      </div>

      {ActionMenuPortal}
      {EditDialog}
      {DeleteDialog}
    </div>
  );
};
