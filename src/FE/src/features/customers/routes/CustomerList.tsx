import { useCallback, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Building2, ChevronRight, Mail, MapPin, MoreHorizontal, Phone, Plus, Users } from 'lucide-react';
import { type CustomerListItemViewModel } from '../../../api/generated/models';
import { ErrorState } from '../../../components/ErrorState';
import { SearchBar } from '../../../components/filters/SearchBar';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { useInfiniteList } from '../../../hooks/useInfiniteList';
import { useInfiniteScroll } from '../../../hooks/useInfiniteScroll';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
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
  const [sortBy, setSortBy] = useState('');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
  const [viewPage, setViewPage] = useState(1);
  const isDesktop = useMediaQuery('(min-width: 768px)');

  const handleSort = (field: string) => {
    if (sortBy === field) {
      setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(field);
      setSortDirection('asc');
    }
  };

  const fetchCustomersPage = useCallback(
    async ({ limit, offset }: { limit: number; offset: number }) => {
      const data = await apiClient.get('/api/customers', {
        params: { limit, offset },
      }) as CustomerListItemViewModel[];
      return { items: data, totalCount: data.length };
    },
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

  const searched = useSearch(query.items, search, (customer, term) =>
    [customer.name, customer.address, customer.email, customer.contactPerson, customer.phone].some((value) => value?.toLowerCase().includes(term)),
  );

  const customers = useMemo(() => {
    if (!sortBy) return searched;
    return [...searched].sort((a, b) => {
      let cmp = 0;
      switch (sortBy) {
        case 'name':
          cmp = (a.name || '').localeCompare(b.name || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'address':
          cmp = (a.address || '').localeCompare(b.address || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'email':
          cmp = (a.email || '').localeCompare(b.email || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'contactPerson':
          cmp = (a.contactPerson || '').localeCompare(b.contactPerson || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'jobCount':
          cmp = Number(a.jobCount ?? 0) - Number(b.jobCount ?? 0);
          break;
      }
      return sortDirection === 'asc' ? cmp : -cmp;
    });
  }, [searched, sortBy, sortDirection]);

  const totalPages = Math.max(1, Math.ceil(customers.length / PAGE_SIZE));
  const safeViewPage = Math.min(viewPage, totalPages);
  const pageStart = (safeViewPage - 1) * PAGE_SIZE;
  const pageEnd = pageStart + PAGE_SIZE;
  const displayedCustomers = isDesktop ? customers.slice(pageStart, pageEnd) : customers;

  const {
    toggleActionMenu,
    openActionMenu,
    ActionMenuPortal,
    EditDialog,
    DeleteDialog,
  } = useCustomerActions({
    customers,
    onEditCustomer: (customer) => navigate(`/app/customers/${customer.id}/edit`),
  });

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
        <ErrorState message="Kunne ikke hente kunder. Prøv igen." onRetry={() => void query.refetch()} />
      </div>
    );
  }

  return (
    <div className="page-container">
      <div className="page-header">
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem' }}>
          <div>
            <h2>Kunder</h2>
            <p className="subtitle">{customers.length} {customers.length === 1 ? 'kunde' : 'kunder'}</p>
          </div>
          <button className="btn btn-primary" onClick={() => navigate('/app/customers/new')} type="button">
            <Plus size={18} />
            <span>Ny kunde</span>
          </button>
        </div>
      </div>

      <SearchBar value={search} onChange={setSearch} placeholder="Søg kunder..." />
      <div className="search-bar-spacer" />

      {isDesktop ? (
        <>
        <table className="data-table">
          <thead>
            <tr>
              <th className={`sortable${sortBy === 'name' ? ' sorted' : ''}`} onClick={() => handleSort('name')}>
                Navn<span className="sort-icon">{sortBy === 'name' ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}</span>
              </th>
              <th className={`sortable${sortBy === 'address' ? ' sorted' : ''}`} onClick={() => handleSort('address')}>
                Adresse<span className="sort-icon">{sortBy === 'address' ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}</span>
              </th>
              <th className={`sortable${sortBy === 'email' ? ' sorted' : ''}`} onClick={() => handleSort('email')}>
                Email<span className="sort-icon">{sortBy === 'email' ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}</span>
              </th>
              <th className={`sortable${sortBy === 'contactPerson' ? ' sorted' : ''}`} onClick={() => handleSort('contactPerson')}>
                Kontakt<span className="sort-icon">{sortBy === 'contactPerson' ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}</span>
              </th>
              <th className={`sortable${sortBy === 'jobCount' ? ' sorted' : ''} col-hours`} onClick={() => handleSort('jobCount')}>
                Sager<span className="sort-icon">{sortBy === 'jobCount' ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}</span>
              </th>
              <th className="col-actions"></th>
            </tr>
          </thead>
          <tbody>
            {displayedCustomers.map((customer) => (
              <tr
                key={customer.id}
                className="clickable"
                onClick={() => navigate(`/app/customers/${customer.id}`)}
              >
                <td>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <Building2 size={16} style={{ color: 'var(--text-muted)', flexShrink: 0 }} />
                    <span>{customer.name}</span>
                  </div>
                </td>
                <td>{customer.address || '—'}</td>
                <td>{customer.email || '—'}</td>
                <td>{customer.contactPerson || '—'}</td>
                <td className="cell-number">{customer.jobCount}</td>
                <td className="col-actions">
                  <div style={{ display: 'flex', gap: '0.25rem', justifyContent: 'flex-end' }}>
                    <button
                      type="button"
                      className="btn-icon"
                      style={{ opacity: 0.5 }}
                      onClick={(e) => {
                        e.stopPropagation();
                        toggleActionMenu(e, customer.id);
                      }}
                      aria-label="Handlinger"
                      title="Handlinger"
                    >
                      <MoreHorizontal size={16} />
                    </button>
                    <ChevronRight size={16} className="row-link-icon" />
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <PaginationControls
          page={safeViewPage}
          totalCount={customers.length}
          pageSize={PAGE_SIZE}
          hasNextPage={query.hasNextPage ?? false}
          isFetchingNextPage={query.isFetchingNextPage}
          onPrev={() => setViewPage((p) => p - 1)}
          onNext={() => setViewPage((p) => p + 1)}
          onLoadMore={() => { void query.fetchNextPage(); }}
        />
        </>
      ) : (
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
                </div>

                <div className="job-card-body">
                  <span className="meta-item customer-job-count">
                     {customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}
                  </span>
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

          {!isDesktop && (
            <InfiniteScrollSentinel
              sentinelRef={sentinelRef}
              isLoading={query.isFetchingNextPage}
            />
          )}
        </div>
      )}

      {ActionMenuPortal}
      {EditDialog}
      {DeleteDialog}
    </div>
  );
};
