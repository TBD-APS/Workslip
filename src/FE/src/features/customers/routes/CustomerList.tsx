import { useCallback, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronRight, Heart, Loader2, Mail, MapPin, MoreHorizontal, Phone, Plus, TrendingUp, Upload, Users } from 'lucide-react';
import { createPortal } from 'react-dom';
import { type CustomerListItemViewModel } from '../../../api/generated/models';
import { Can } from '../../../providers/permissions/Can';
import { ErrorState } from '../../../components/ErrorState';
import { CopyAddressButton } from '../../../components/CopyAddressButton';
import { SearchBar } from '../../../components/filters/SearchBar';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { usePaginatedList } from '../../../hooks/usePaginatedList';
import { useColumnResize } from '../../../hooks/useColumnResize';
import { apiClient } from '../../../lib/axios';
import { useCustomerActions } from '../components/CustomerActions';
import { getApiCustomersFavorite } from '../../jobs/customerApi';
import { getGetApiCustomersQueryKey } from '../../../api/generated/customers/customers';
import { notify } from '../../../lib/toast';

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
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [pendingImport, setPendingImport] = useState<File | null>(null);
  const [isImporting, setIsImporting] = useState(false);

  const handleImport = async () => {
    if (!pendingImport) return;
    setIsImporting(true);
    try {
      const formData = new FormData();
      formData.append('file', pendingImport);
      const result = await apiClient.post('/api/customers/import', formData) as { imported: number; duplicates: number; skipped: number; failed: number };
      void queryClient.invalidateQueries({ queryKey: getGetApiCustomersQueryKey() });
      notify.success(
        `${result.imported} importeret, ${result.duplicates} dubletter, ${result.skipped} sprunget over, ${result.failed} med fejl.`,
      );
      setPendingImport(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
    } catch {
      // Toast handled by axios interceptor.
    } finally {
      setIsImporting(false);
    }
  };

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

  const { data: favoriteCustomers = [] } = useQuery({
    queryKey: ['customers', 'favorite', 5],
    queryFn: () => getApiCustomersFavorite({ limit: 5 }),
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
              <div className="flex-row-center gap-sm">
                <Can permission="customer:edit">
                  <>
                    <input
                      ref={fileInputRef}
                      type="file"
                      accept=".xlsx,.csv"
                      hidden
                      onChange={(e) => setPendingImport(e.target.files?.[0] ?? null)}
                    />
                    <button className="btn btn-secondary" type="button" onClick={() => fileInputRef.current?.click()}>
                      <Upload size={16} />
                      <span>Importér</span>
                    </button>
                    <button className="btn btn-primary" onClick={() => navigate('/app/customers/new')} type="button">
                      <Plus size={18} />
                      <span>Ny kunde</span>
                    </button>
                  </>
                </Can>
              </div>
            )}
          </div>
        )}
      </div>

      <SearchBar value={search} onChange={handleSearchChange} placeholder="Søg kunder..." />

      {favoriteCustomers.length > 0 && !search && (
        <div className="favorite-customers-section">
          <div className="favorite-customers-header">
            <TrendingUp size={16} />
            <span>Favoritkunder</span>
          </div>
          <div className="favorite-customers-grid">
            {favoriteCustomers.map((customer) => (
              <button
                key={customer.id}
                className="favorite-customer-card"
                onClick={() => navigate(`/app/customers/${customer.id}`)}
                type="button"
              >
                <span className="favorite-customer-name">{customer.name}</span>
                {customer.contactPerson && (
                  <span className="favorite-customer-contact">{customer.contactPerson}</span>
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
                <th className="col-number">Kundenummer</th>
                <th className="col-address">Adresse</th>
                <th className="col-email">Email</th>
                <th className="col-contact">Kontakt</th>
                <th className="col-phone">Telefon</th>
                <th className="col-hours">Sager</th>
                <th className="col-actions" />
              </tr>
            </thead>
            <tbody>
              {Array.from({ length: 5 }).map((_, i) => (
                <tr key={i}>
                  <td><div className="skeleton skeleton-w-70" /></td>
                  <td><div className="skeleton skeleton-w-30" /></td>
                  <td><div className="skeleton skeleton-w-60" /></td>
                  <td><div className="skeleton skeleton-w-50" /></td>
                  <td><div className="skeleton skeleton-w-40" /></td>
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
                <button type="button" className="sort-trigger" onClick={() => handleSort('name')}>
                  Navn<span className="sort-icon">{sortBy === 'name' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(0, e)} />
              </th>
              <th className="col-number">
                <span>Kundenummer</span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(1, e)} />
              </th>
              <th className={`col-address sortable${sortBy === 'address' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('address')}>
                  Adresse<span className="sort-icon">{sortBy === 'address' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(2, e)} />
              </th>
              <th className={`col-email sortable${sortBy === 'email' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('email')}>
                  Email<span className="sort-icon">{sortBy === 'email' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(3, e)} />
              </th>
              <th className={`col-contact sortable${sortBy === 'contactPerson' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('contactPerson')}>
                  Kontakt<span className="sort-icon">{sortBy === 'contactPerson' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(4, e)} />
              </th>
              <th className={`col-phone sortable${sortBy === 'phone' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('phone')}>
                  Telefon<span className="sort-icon">{sortBy === 'phone' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(5, e)} />
              </th>
              <th className={`col-hours sortable${sortBy === 'jobCount' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('jobCount')}>
                  Sager<span className="sort-icon">{sortBy === 'jobCount' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(6, e)} />
              </th>
              <th className="col-actions">
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(7, e)} />
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
                    <span>{customer.name}</span>
                  </div>
                </td>
                <td>{customer.customerNumber}</td>
                <td>
                  <span>{customer.address}</span>
                  <CopyAddressButton address={customer.address} />
                </td>
                <td>{customer.email}</td>
                <td>{customer.contactPerson}</td>
                <td>{customer.phone}</td>
                <td className="cell-number">{customer.jobCount}</td>
                <td className="col-actions">
                  <div className="flex-row-end">
                    <span className={`btn-icon ${customer.isFavorite ? 'text-red' : 'opacity-30'}`} title={customer.isFavorite ? 'Favorit' : ''}>
                        <Heart size={16} fill={customer.isFavorite ? 'currentColor' : 'none'} />
                      </span>
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
          {pageItems.map((customer) => {
            const openCustomer = () => navigate(`/app/customers/${customer.id}`);

            return (
              <div key={customer.id} className="job-card-wrapper">
                <div
                  className="job-card"
                  onClick={openCustomer}
                  onKeyDown={(event) => {
                    if (event.target !== event.currentTarget) return;
                    if (event.key === 'Enter' || event.key === ' ') openCustomer();
                  }}
                  role="link"
                  tabIndex={0}
                >
                  <div className="job-card-top job-card-top-center">
                    <div className="customer-card-identity">
                      <h3 className="customer-name">{customer.name}</h3>
                      {customer.customerNumber && (
                        <span className="text-muted customer-number">#{customer.customerNumber}</span>
                      )}
                    </div>
                  </div>

                  <div className="job-card-body">
                    <span className="meta-item customer-job-count">
                      {customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}
                    </span>
                    {customer.address && (
                      <span className="meta-item">
                        <MapPin size={14} />
                        <span>{customer.address}</span>
                        <CopyAddressButton address={customer.address} />
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
                    <span className={`btn-icon ${customer.isFavorite ? 'text-red' : 'opacity-30'}`} title={customer.isFavorite ? 'Favorit' : ''}>
                      <Heart size={18} fill={customer.isFavorite ? 'currentColor' : 'none'} />
                    </span>
                    <span className="btn-icon" aria-label="Se kunde">
                      <ChevronRight size={20} />
                    </span>
                  </div>
                </div>

                <Can permission="customer:edit">
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
            );
          })}

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
      {pendingImport && (
        <CustomerImportConfirmDialog
          file={pendingImport}
          isImporting={isImporting}
          onConfirm={() => void handleImport()}
          onClose={() => setPendingImport(null)}
        />
      )}
    </div>
  );
};

function CustomerImportConfirmDialog({ file, isImporting, onConfirm, onClose }: { file: File; isImporting: boolean; onConfirm: () => void; onClose: () => void }) {
  return createPortal(
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="customer-import-title">
      <div className="modal-card">
        <h3 id="customer-import-title">Godkend kundeimport</h3>
        <p>Importér kunder fra <strong>{file.name}</strong>?</p>
        <p className="subtitle">Rækker med eksisterende kundenummer springes over. Importen kan ikke fortrydes samlet.</p>
        <div className="modal-actions">
          <button className="btn btn-primary" type="button" onClick={onConfirm} disabled={isImporting}>
            {isImporting && <Loader2 className="animate-spin" size={16} />}
            <span>{isImporting ? 'Importerer...' : 'Importér'}</span>
          </button>
          <button className="btn btn-secondary" type="button" onClick={onClose} disabled={isImporting}>Annuller</button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
