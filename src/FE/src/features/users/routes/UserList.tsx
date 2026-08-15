import { useCallback, useEffect } from 'react';
import { useQueries } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronRight, Clock, Mail } from 'lucide-react';
import { type UserListViewModel, type UserViewModel } from '../../../api/generated/models';
import { getGetApiJobCostingUsersIdRateQueryOptions } from '../../../api/generated/job-costing/job-costing';
import { ErrorState } from '../../../components/ErrorState';
import { SearchBar } from '../../../components/filters/SearchBar';
import { announceSection } from '../../../components/filters/StatusFilter';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { usePaginatedList } from '../../../hooks/usePaginatedList';
import { useColumnResize } from '../../../hooks/useColumnResize';
import { apiClient } from '../../../lib/axios';
import { useAuth } from '../../../providers/useAuth';
import { UserRateEditor } from '../components/UserRateCard';
import { UserRoleBadge } from '../components/UserRoleBadge';

const PAGE_SIZE = 20;
const SHOW_SEARCH = false;

type UserListItem = UserViewModel & {
  roleDisplayName?: string | null;
};

type UserListResponse = Omit<UserListViewModel, 'users'> & {
  users: UserListItem[];
};

function formatHours(value: number | string | null): string {
  if (value == null) return '\u2013';
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (Number.isNaN(num)) return '\u2013';
  return `${num.toLocaleString('da-DK', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} t`;
}

const SkeletonCard = () => (
  <div className="job-card job-card-skeleton" aria-hidden="true">
    <div className="job-card-header">
      <div className="skeleton skeleton-name" style={{ width: '60%' }} />
    </div>
    <div className="skeleton skeleton-address" style={{ width: '40%' }} />
    <div className="skeleton skeleton-tag" style={{ width: '30%' }} />
    <div className="skeleton skeleton-address" style={{ width: '52%' }} />
  </div>
);

export const UserList = () => {
  const navigate = useNavigate();
  const { user: currentUser } = useAuth();

  const fetchUsersPage = useCallback(async ({ limit, offset, search, sortBy, sortDirection }: { limit: number; offset: number; search?: string; sortBy?: string; sortDirection?: string }) => {
    const response = (await apiClient.get('/api/users', {
      params: { limit, offset, search, sortBy, sortDirection },
    })) as UserListResponse;

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
  } = usePaginatedList<UserListItem>({
    queryKey: [
      '/api/users',
      'list',
      {
        actorId: currentUser?.id ?? null,
        organizationId: currentUser?.organizationId ?? null,
        role: currentUser?.role ?? null,
      },
    ],
    fetchPage: fetchUsersPage,
    pageSize: PAGE_SIZE,
    storageKey: 'users',
  });

  useEffect(() => {
    announceSection('users');
  }, []);

  const { handleMouseDown } = useColumnResize();

  const rateQueries = useQueries({
    queries: pageItems.map((user) => getGetApiJobCostingUsersIdRateQueryOptions(user.id, {
      query: { staleTime: 60_000 },
    })),
  });

  const getRateState = (index: number) => {
    const rateQuery = rateQueries[index];
    return {
      rate: rateQuery?.data?.billableHourlyRate ?? null,
      isLoading: rateQuery?.isPending ?? false,
      isError: rateQuery?.isError ?? false,
    };
  };

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

      {SHOW_SEARCH && <SearchBar value={search} onChange={handleSearchChange} placeholder="Søg brugere..." />}

      {isErrored ? (
        <ErrorState message="Kunne ikke hente brugere. Prøv igen." onRetry={() => void refetch()} />
      ) : showLoadingSkeleton || showPageLoading ? (
        isDesktop ? (
          <table className="data-table">
            <thead>
              <tr>
                <th className="col-name">Navn</th>
                <th className="col-email">Email</th>
                <th className="col-role">Rolle</th>
                <th className="col-hours">Uge</th>
                <th>Timepris</th>
                <th className="col-actions" />
              </tr>
            </thead>
            <tbody>
              {Array.from({ length: 5 }).map((_, index) => (
                <tr key={index}>
                  <td><div className="skeleton skeleton-w-60" /></td>
                  <td><div className="skeleton skeleton-w-70" /></td>
                  <td><div className="skeleton skeleton-w-40" /></td>
                  <td><div className="skeleton skeleton-w-1-5rem" /></td>
                  <td><div className="skeleton skeleton-w-60" /></td>
                  <td><div className="skeleton skeleton-w-1-5rem" /></td>
                </tr>
              ))}
            </tbody>
          </table>
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
                      <button type="button" className="sort-trigger" onClick={() => handleSort('displayName')}>
                        Navn<span className="sort-icon">{sortBy === 'displayName' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                      </button>
                      <div className="col-resize-handle" onMouseDown={(event) => handleMouseDown(0, event)} />
                    </th>
                    <th className={`col-email sortable${sortBy === 'email' ? ' sorted' : ''}`}>
                      <button type="button" className="sort-trigger" onClick={() => handleSort('email')}>
                        Email<span className="sort-icon">{sortBy === 'email' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                      </button>
                      <div className="col-resize-handle" onMouseDown={(event) => handleMouseDown(1, event)} />
                    </th>
                    <th className={`col-role sortable${sortBy === 'role' ? ' sorted' : ''}`}>
                      <button type="button" className="sort-trigger" onClick={() => handleSort('role')}>
                        Rolle<span className="sort-icon">{sortBy === 'role' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                      </button>
                      <div className="col-resize-handle" onMouseDown={(event) => handleMouseDown(2, event)} />
                    </th>
                    <th className="col-hours">Uge</th>
                    <th>Timepris</th>
                    <th className="col-actions">
                      <div className="col-resize-handle" onMouseDown={(event) => handleMouseDown(3, event)} />
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {pageItems.map((user, index) => {
                    const rateState = getRateState(index);
                    return (
                      <tr
                        key={user.id}
                        className="clickable"
                        tabIndex={0}
                        onClick={() => navigate(`/app/users/${user.id}`)}
                        onKeyDown={(event) => {
                          if (event.target !== event.currentTarget) return;
                          if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault();
                            navigate(`/app/users/${user.id}`);
                          }
                        }}
                      >
                        <td><strong>{user.displayName}</strong></td>
                        <td>
                          <span className="inline-flex-center">
                            <Mail size={14} className="text-muted" aria-hidden="true" />
                            {user.email}
                          </span>
                        </td>
                        <td>
                          <UserRoleBadge role={user.role} displayName={user.roleDisplayName} />
                        </td>
                        <td className="col-hours">{formatHours(user.hoursThisWeek)}</td>
                        <td className="user-rate-table-cell">
                          <UserRateEditor
                            userId={user.id}
                            rate={rateState.rate}
                            isLoading={rateState.isLoading}
                            isError={rateState.isError}
                            variant="inline"
                            ariaLabel={`Fakturerbar timepris for ${user.displayName}`}
                          />
                        </td>
                        <td className="col-actions">
                          <ChevronRight size={16} className="row-link-icon" aria-hidden="true" />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
              <PaginationControls
                page={safeViewPage}
                totalCount={totalCount}
                pageSize={PAGE_SIZE}
                onPrev={() => setViewPage((page) => Math.max(1, page - 1))}
                onNext={() => {
                  const nextPage = safeViewPage + 1;
                  if (nextPage > totalPages) return;
                  setViewPage(nextPage);
                }}
              />
            </>
          ) : (
            <div className="job-list">
              {pageItems.map((user, index) => {
                const rateState = getRateState(index);
                return (
                  <div key={user.id} className="job-card user-card-with-rate">
                    <button
                      className="user-card-primary-action"
                      onClick={() => navigate(`/app/users/${user.id}`)}
                      type="button"
                      aria-label={`Åbn ${user.displayName}`}
                    >
                      <div className="job-card-top">
                        <div>
                          <h3 className="job-customer">{user.displayName}</h3>
                        </div>
                      </div>

                      <div className="job-card-meta">
                        <span className="meta-item">
                          <Mail size={14} aria-hidden="true" />
                          <span>{user.email}</span>
                        </span>
                        <UserRoleBadge role={user.role} displayName={user.roleDisplayName} />
                      </div>

                      <div className="user-hours-row">
                        <Clock size={14} className="text-muted" aria-hidden="true" />
                        <span>{formatHours(user.hoursThisWeek)} denne uge</span>
                      </div>
                    </button>

                    <div className="user-rate-mobile-row">
                      <UserRateEditor
                        userId={user.id}
                        rate={rateState.rate}
                        isLoading={rateState.isLoading}
                        isError={rateState.isError}
                        variant="inline"
                        ariaLabel={`Fakturerbar timepris for ${user.displayName}`}
                      />
                    </div>

                    <div className="job-card-footer">
                      <span />
                      <button
                        type="button"
                        className="btn-icon"
                        aria-label={`Se ${user.displayName}`}
                        onClick={() => navigate(`/app/users/${user.id}`)}
                      >
                        <ChevronRight size={20} aria-hidden="true" />
                      </button>
                    </div>
                  </div>
                );
              })}

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
        </>
      )}
    </div>
  );
};