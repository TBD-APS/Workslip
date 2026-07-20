import { useCallback } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { ArrowDown, ArrowUp, ArrowUpDown, Building2, ChevronRight, Mail, MapPin, MoreHorizontal, Phone, Plus, Star, TrendingUp, Users } from 'lucide-react';
import { type CustomerListItemViewModel } from '../../../api/generated/models';
import { Can } from '../../../providers/permissions/Can';
import { ErrorState } from '../../../components/ErrorState';
import { SearchBar } from '../../../components/filters/SearchBar';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { usePaginatedList } from '../../../hooks/usePaginatedList';
import { useColumnResize } from '../../../hooks/useColumnResize';
import { apiClient } from '../../../lib/axios';
import { useCustomerActions } from '../components/CustomerActions';
import { getApiCustomersTop, patchApiCustomersIdTop } from '../../jobs/customerApi';

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

  const fetchCustomersPage = useCallback(
    async ({ limit, offset, search, sortBy, sortDirection }: { limit: number; offset: number; search?: string; sortBy?: string; sortDirection?: string }) => {
      const data = await apiClient.get('/api/customers', {
        params: { limit, offset, search, sortBy, sortDirection },
      }) as { items: CustomerListItemViewModel[]; totalCount: number };
      return data;
    },
    [],
  );

  const {
    items: customers,
    totalCount,
    isLoading,
    isFetching,
    isError,
    isFetchingNextPage,
    refetch,
    search,
    handleSearchChange,
    sortBy,
    sortDirection,
    handleSort,
    setViewPage,
    totalPages,
    safeViewPage,
    pageItems,
    sentinelRef,
    isDesktop,
  } = usePaginatedList<CustomerListItemViewModel>({
    queryKey: ['/api/customers'],
    fetchPage: fetchCustomersPage,
    pageSize: PAGE_SIZE,
    storageKey: 'customers',
  });

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

  const { handleMouseDown } = useColumnResize();

  const { data: topCustomers = [] } = useQuery({
    queryKey: ['customers', 'top', 5],
    queryFn: () => getApiCustomersTop({ limit: 5 }),
  });

  const queryClient = useQueryClient();
  const toggleTopMutation = useMutation({
    mutationFn: ({ id, isTop }: { id: string; isTop: boolean }) =>
      patchApiCustomersIdTop(id, { isTop }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['customers'] });
      void queryClient.invalidateQueries({ queryKey: ['customers', 'top'] });
    },
  });

  const showLoadingSkeleton = isLoading && customers.length === 0;
  const isErrored = isError && customers.length === 0;
  const showPageLoading = isDesktop && isFetching && !showLoadingSkeleton && customers.length < safeViewPage * PAGE_SIZE;

  return (
    <div className="page-container">
      {isFetching && <div className="data-table-loading-bar" />}
      <div className="page-header">
        {showLoadingSkeleton ? (
          <>
            <div className="skeleton skeleton-title" />
            <div className="skeleton skeleton-subtitle" />
          </>
        ) : (
          <div className="flex-row-between">
            <div>
              <h2>Kunder</h2>
              <p className="subtitle">{totalCount} {totalCount === 1 ? 'kunde' : 'kunder'}</p>
            </div>
            {isDesktop && (
              <button className="btn btn-primary" onClick={() => navigate('/app/customers/new')} type="button">
                <Plus size={18} />
                <span>Ny kunde</span>
              </button>
            )}
          </div>
        )}
      </div>

      <SearchBar value={search} onChange={handleSearchChange} placeholder="Søg kunder..." />

      {topCustomers.length > 0 && !search && (
        <div className="top-customers-section">
          <div className="top-customers-header">
            <TrendingUp size={16} />
            <span>Mest aktive kunder</span>
          </div>
          <div className="top-customers-grid">
            {topCustomers.map((customer) => (
              <button
                key={customer.id}
                className="top-customer-card"
                onClick={() => navigate(`/app/customers/${customer.id}`)}
                type="button"
              >
                <Building2 size={16} className="top-customer-icon" />
                <span className="top-customer-name">{customer.name}</span>
                {customer.contactPerson && (
                  <span className="top-customer-contact">{customer.contactPerson}</span>
                )}
              </button>
            ))}
          </div>
        </div>
      )}

      {isErrored ? (
        <ErrorState message="Kunne ikke hente kunder. Prøv igen." onRetry={() => void refetch()} />
      ) : showLoadingSkeleton || showPageLoading ? (
        isDesktop ? (
          <>
          <table className="data-table">
            <thead>
              <tr>
                <th className="col-name">Navn</th>
                <th className="col-address">Adresse</th>
                <th className="col-email">Email</th>
                <th className="col-contact">Kontakt</th>
                <th className="col-hours">Sager</th>
                <th className="col-actions" />
              </tr>
            </thead>
            <tbody>
              {Array.from({ length: 5 }).map((_, i) => (
                <tr key={i}>
                  <td><div className="skeleton skeleton-w-70" /></td>
                  <td><div className="skeleton skeleton-w-60" /></td>
                  <td><div className="skeleton skeleton-w-50" /></td>
                  <td><div className="skeleton skeleton-w-40" /></td>
                  <td><div className="skeleton skeleton-w-2rem" /></td>
                  <td><div className="skeleton skeleton-w-1-5rem" /></td>
                </tr>
              ))}
            </tbody>
          </table>
          </>
        ) : (
          <div className="job-list">
            <SkeletonCard />
            <SkeletonCard />
            <SkeletonCard />
          </div>
        )
      ) : (
        <>
        {isDesktop ? (
          <>
          <table className="data-table">
          <thead>
            <tr>
              <th className={`col-name sortable${sortBy === 'name' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('name')}>
                  Navn<span className="sort-icon">{sortBy === 'name' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(0, e)} />
              </th>
              <th className={`col-address sortable${sortBy === 'address' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('address')}>
                  Adresse<span className="sort-icon">{sortBy === 'address' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(1, e)} />
              </th>
              <th className={`col-email sortable${sortBy === 'email' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('email')}>
                  Email<span className="sort-icon">{sortBy === 'email' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(2, e)} />
              </th>
              <th className={`col-contact sortable${sortBy === 'contactPerson' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('contactPerson')}>
                  Kontakt<span className="sort-icon">{sortBy === 'contactPerson' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(3, e)} />
              </th>
              <th className={`col-hours sortable${sortBy === 'jobCount' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('jobCount')}>
                  Sager<span className="sort-icon">{sortBy === 'jobCount' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(4, e)} />
              </th>
              <th className="col-actions">
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(5, e)} />
              </th>
            </tr>
          </thead>
          <tbody>
            {pageItems.map((customer) => (
              <tr
                key={customer.id}
                className="clickable"
                onClick={() => navigate(`/app/customers/${customer.id}`)}
              >
                <td>
                  <div className="flex-row-center">
                    <Building2 size={16} className="text-muted flex-shrink-0" />
                    <span>{customer.name}</span>
                  </div>
                </td>
                <td>{customer.address}</td>
                <td>{customer.email}</td>
                <td>{customer.contactPerson}</td>
                <td className="cell-number">{customer.jobCount}</td>
                <td className="col-actions">
                  <div className="flex-row-end">
                    <Can
                      permission="customer:edit"
                      fallback={
                        <span className={`btn-icon ${customer.isTop ? 'text-amber' : 'opacity-30'}`} title={customer.isTop ? 'Top kunde' : ''}>
                          <Star size={16} fill={customer.isTop ? 'currentColor' : 'none'} />
                        </span>
                      }
                    >
                      <button
                        type="button"
                        className={`btn-icon ${customer.isTop ? 'text-amber' : 'opacity-30'}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          toggleTopMutation.mutate({ id: customer.id, isTop: !customer.isTop });
                        }}
                        aria-label={customer.isTop ? 'Fjern fra top' : 'Tilføj til top'}
                        title={customer.isTop ? 'Fjern fra top' : 'Tilføj til top'}
                      >
                        <Star size={16} fill={customer.isTop ? 'currentColor' : 'none'} />
                      </button>
                    </Can>
                    <Can permission="customer:edit">
                      <button
                        type="button"
                        className="btn-icon opacity-50"
                        onClick={(e) => {
                          e.stopPropagation();
                          toggleActionMenu(e, customer.id);
                        }}
                        aria-label="Handlinger"
                        title="Handlinger"
                      >
                        <MoreHorizontal size={16} />
                      </button>
                    </Can>
                    <ChevronRight size={16} className="row-link-icon" />
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <PaginationControls
          page={safeViewPage}
          totalCount={totalCount}
          pageSize={PAGE_SIZE}
          onPrev={() => setViewPage((p) => Math.max(1, p - 1))}
          onNext={() => {
            const nextPage = safeViewPage + 1;
            if (nextPage > totalPages) return;
            setViewPage(nextPage);
          }}
        />
        </>
      ) : (
        <div className="job-list">
          {pageItems.map((customer) => (
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
                  <Can
                    permission="customer:edit"
                    fallback={
                      <span className={`btn-icon ${customer.isTop ? 'text-amber' : 'opacity-30'}`} title={customer.isTop ? 'Top kunde' : ''}>
                        <Star size={18} fill={customer.isTop ? 'currentColor' : 'none'} />
                      </span>
                    }
                  >
                    <button
                      type="button"
                      className={`btn-icon ${customer.isTop ? 'text-amber' : 'opacity-30'}`}
                      onClick={(e) => {
                        e.stopPropagation();
                        toggleTopMutation.mutate({ id: customer.id, isTop: !customer.isTop });
                      }}
                      aria-label={customer.isTop ? 'Fjern fra top' : 'Tilføj til top'}
                      title={customer.isTop ? 'Fjern fra top' : 'Tilføj til top'}
                    >
                      <Star size={18} fill={customer.isTop ? 'currentColor' : 'none'} />
                    </button>
                  </Can>
                  <span className="btn-icon" aria-label="Se kunde">
                    <ChevronRight size={20} />
                  </span>
                </div>
              </button>

              <Can permission="user:manage">
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
              </Can>
            </div>
          ))}

          {customers.length === 0 && !isFetchingNextPage && (
            <div className="empty-state">
              <p>Ingen kunder fundet.</p>
            </div>
          )}

          {!isDesktop && (
            <InfiniteScrollSentinel
              sentinelRef={sentinelRef}
              isLoading={isFetchingNextPage}
            />
          )}
        </div>
      )}
      </>)}

      {ActionMenuPortal}
      {EditDialog}
      {DeleteDialog}
    </div>
  );
};
