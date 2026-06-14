import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AlertCircle, ChevronRight, Mail, Shield } from 'lucide-react';
import { type UserListViewModel, type UserViewModel } from '../../../api/generated/models';
import { SearchBar } from '../../../components/filters/SearchBar';
import { announceSection } from '../../../components/filters/StatusFilter';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { useInfiniteList } from '../../../hooks/useInfiniteList';
import { useInfiniteScroll } from '../../../hooks/useInfiniteScroll';
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

  const fetchUsersPage = useCallback(async ({ limit, offset }: { limit: number; offset: number }) => {
    const response = (await apiClient.get('/api/users', {
      params: { limit, offset },
    })) as UserListViewModel;

    return response.users;
  }, []);

  const query = useInfiniteList<UserViewModel>({
    queryKey: ['/api/users', { limit: PAGE_SIZE }],
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

  const users = useSearch(query.items, search, (user, term) =>
    [user.displayName, user.email, user.phone, user.role].some((value) => value?.toLowerCase().includes(term)),
  );

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
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente brugere. Prøv igen.</p>
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
        <h2>Folk</h2>
        <p className="subtitle">{users.length} {users.length === 1 ? 'bruger' : 'brugere'}</p>
      </div>

      <SearchBar value={search} onChange={setSearch} placeholder="Søg brugere..." />
      <div className="search-bar-spacer" />

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

        <InfiniteScrollSentinel
          sentinelRef={sentinelRef}
          isLoading={query.isFetchingNextPage}
        />
      </div>
    </div>
  );
};
