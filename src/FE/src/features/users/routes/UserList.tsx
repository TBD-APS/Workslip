import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ChevronRight, Mail, Shield } from 'lucide-react';
import { type UserListViewModel, type UserViewModel } from '../../../api/generated/models';
import { ErrorState } from '../../../components/ErrorState';
import { SearchBar } from '../../../components/filters/SearchBar';
import { announceSection } from '../../../components/filters/StatusFilter';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { useInfiniteList } from '../../../hooks/useInfiniteList';
import { useInfiniteScroll } from '../../../hooks/useInfiniteScroll';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
import { useSearch } from '../../../hooks/useSearch';
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

  const fetchUsersPage = useCallback(async ({ limit, offset }: { limit: number; offset: number }) => {
    const response = (await apiClient.get('/api/users', {
      params: { limit, offset },
    })) as UserListViewModel;

    return { items: response.users, totalCount: Number(response.total) };
  }, []);

  const query = useInfiniteList<UserViewModel>({
    queryKey: ['/api/users', 'list', { limit: PAGE_SIZE }],
    fetchPage: fetchUsersPage,
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

  const searched = useSearch(query.items, search, (user, term) =>
    [user.displayName, user.email, user.phone, user.role].some((value) => value?.toLowerCase().includes(term)),
  ) ?? [];

  const users = useMemo(() => {
    if (!sortBy) return searched;
    return [...searched].sort((a, b) => {
      let cmp = 0;
      switch (sortBy) {
        case 'displayName':
          cmp = (a.displayName || '').localeCompare(b.displayName || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'email':
          cmp = (a.email || '').localeCompare(b.email || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'role':
          cmp = (a.role || '').localeCompare(b.role || '', 'da-DK', { sensitivity: 'base' });
          break;
      }
      return sortDirection === 'asc' ? cmp : -cmp;
    });
  }, [searched, sortBy, sortDirection]);

  const totalPages = Math.max(1, Math.ceil(users.length / PAGE_SIZE));
  const safeViewPage = Math.min(viewPage, totalPages);
  const pageStart = (safeViewPage - 1) * PAGE_SIZE;
  const pageEnd = pageStart + PAGE_SIZE;
  const displayedUsers = isDesktop ? users.slice(pageStart, pageEnd) : users;

  useEffect(() => {
    announceSection('users');
  }, []);

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
        <ErrorState message="Kunne ikke hente brugere. Prøv igen." onRetry={() => void query.refetch()} />
      </div>
    );
  }

  return (
    <div className="page-container">
      <div className="page-header">
        <h2>Folk</h2>
        <p className="subtitle">{users.length} {users.length === 1 ? 'bruger' : 'brugere'}</p>
      </div>

      <SearchBar value={search} onChange={setSearch} placeholder="Søg brugere..." />
      <div className="search-bar-spacer" />

      {isDesktop ? (
        <>
        <table className="data-table">
          <thead>
            <tr>
              <th className={`sortable${sortBy === 'displayName' ? ' sorted' : ''}`} onClick={() => handleSort('displayName')}>
                Navn<span className="sort-icon">{sortBy === 'displayName' ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}</span>
              </th>
              <th className={`sortable${sortBy === 'email' ? ' sorted' : ''}`} onClick={() => handleSort('email')}>
                Email<span className="sort-icon">{sortBy === 'email' ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}</span>
              </th>
              <th className={`sortable${sortBy === 'role' ? ' sorted' : ''}`} onClick={() => handleSort('role')}>
                Rolle<span className="sort-icon">{sortBy === 'role' ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}</span>
              </th>
              <th className="col-actions"></th>
            </tr>
          </thead>
          <tbody>
            {displayedUsers.map((user) => (
              <tr
                key={user.id}
                className="clickable"
                onClick={() => navigate(`/app/users/${user.id}`)}
              >
                <td><strong>{user.displayName}</strong></td>
                <td>
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem' }}>
                    <Mail size={14} style={{ color: 'var(--text-muted)' }} />
                    {user.email}
                  </span>
                </td>
                <td>
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem' }}>
                    <Shield size={14} style={{ color: 'var(--text-muted)' }} />
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
          totalCount={users.length}
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
          {users.map((user) => (
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

          {users.length === 0 && !query.isFetchingNextPage && (
            <div className="empty-state">
              <p>Ingen brugere fundet.</p>
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
    </div>
  );
};
