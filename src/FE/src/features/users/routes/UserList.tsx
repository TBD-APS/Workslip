import { useCallback, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronRight, Mail, Shield } from 'lucide-react';
import { type UserListViewModel, type UserViewModel } from '../../../api/generated/models';
import { ErrorState } from '../../../components/ErrorState';
import { SearchBar } from '../../../components/filters/SearchBar';
import { announceSection } from '../../../components/filters/StatusFilter';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { usePaginatedList } from '../../../hooks/usePaginatedList';
import { useColumnResize } from '../../../hooks/useColumnResize';
import { apiClient } from '../../../lib/axios';

const PAGE_SIZE = 20;

const SkeletonCard = () => (
  <div className="job-card job-card-skeleton" aria-hidden="true">
    <div className="job-card-header">
      <div className="skeleton skeleton-name" style={{ width: '60%' }} />
    </div>
    <div className="skeleton skeleton-address" style={{ width: '40%' }} />
    <div className="skeleton skeleton-tag" style={{ width: '30%' }} />
  </div>
);

export const UserList = () => {
  const navigate = useNavigate();

  const fetchUsersPage = useCallback(async ({ limit, offset, search, sortBy, sortDirection }: { limit: number; offset: number; search?: string; sortBy?: string; sortDirection?: string }) => {
    const response = (await apiClient.get('/api/users', {
      params: { limit, offset, search, sortBy, sortDirection },
    })) as UserListViewModel;

    return { items: response.users, totalCount: Number(response.total) };
  }, []);

  const {
    items: users,
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
  } = usePaginatedList<UserViewModel>({
    queryKey: ['/api/users', 'list'],
    fetchPage: fetchUsersPage,
    pageSize: PAGE_SIZE,
    storageKey: 'users',
  });

  useEffect(() => {
    announceSection('users');
  }, []);

  const { handleMouseDown } = useColumnResize();

  const showLoadingSkeleton = isLoading && users.length === 0;
  const isErrored = isError && users.length === 0;
  const showPageLoading = isDesktop && isFetching && !showLoadingSkeleton && users.length < safeViewPage * PAGE_SIZE;

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
          <>
            <h2>Folk</h2>
            <p className="subtitle">{totalCount} {totalCount === 1 ? 'bruger' : 'brugere'}</p>
          </>
        )}
      </div>

      <SearchBar value={search} onChange={handleSearchChange} placeholder="Søg brugere..." />

      {isErrored ? (
        <ErrorState message="Kunne ikke hente brugere. Prøv igen." onRetry={() => void refetch()} />
      ) : showLoadingSkeleton || showPageLoading ? (
        isDesktop ? (
          <>
          <table className="data-table">
            <thead>
              <tr>
                <th className="col-name">Navn</th>
                <th className="col-email">Email</th>
                <th className="col-role">Rolle</th>
                <th className="col-actions" />
              </tr>
            </thead>
            <tbody>
              {Array.from({ length: 5 }).map((_, i) => (
                <tr key={i}>
                  <td><div className="skeleton skeleton-w-60" /></td>
                  <td><div className="skeleton skeleton-w-70" /></td>
                  <td><div className="skeleton skeleton-w-40" /></td>
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
              <th className={`col-name sortable${sortBy === 'displayName' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('displayName')}>
                  Navn<span className="sort-icon">{sortBy === 'displayName' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(0, e)} />
              </th>
              <th className={`col-email sortable${sortBy === 'email' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('email')}>
                  Email<span className="sort-icon">{sortBy === 'email' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(1, e)} />
              </th>
              <th className={`col-role sortable${sortBy === 'role' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('role')}>
                  Rolle<span className="sort-icon">{sortBy === 'role' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(2, e)} />
              </th>
              <th className="col-actions">
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(3, e)} />
              </th>
            </tr>
          </thead>
          <tbody>
            {pageItems.map((user) => (
              <tr
                key={user.id}
                className="clickable"
                onClick={() => navigate(`/app/users/${user.id}`)}
              >
                <td><strong>{user.displayName}</strong></td>
                <td>
                  <span className="inline-flex-center">
                    <Mail size={14} className="text-muted" />
                    {user.email}
                  </span>
                </td>
                <td>
                  <span className="inline-flex-center">
                    <Shield size={14} className="text-muted" />
                    {user.role}
                  </span>
                </td>
                <td className="col-actions">
                  <ChevronRight size={16} className="row-link-icon" />
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
          {pageItems.map((user) => (
            <button
              key={user.id}
              className="job-card"
              onClick={() => navigate(`/app/users/${user.id}`)}
              type="button"
            >
              <div className="job-card-top">
                <div>
                  <h3 className="job-customer">{user.displayName}</h3>
                </div>
              </div>

              <div className="job-card-meta">
                <span className="meta-item">
                  <Mail size={14} />
                  <span>{user.email}</span>
                </span>
                <span className="meta-item">
                  <Shield size={14} />
                  <span>{user.role}</span>
                </span>
              </div>

              <div className="job-card-footer">
                <span />
                <span className="btn-icon" aria-label="Se bruger">
                  <ChevronRight size={20} />
                </span>
              </div>
            </button>
          ))}

          {users.length === 0 && !isFetchingNextPage && (
            <div className="empty-state">
              <p>Ingen brugere fundet.</p>
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
    </div>
  );
};
