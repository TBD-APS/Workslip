import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { AlertCircle, ChevronRight, MapPin, Timer, User } from 'lucide-react';
import { type JobListItemViewModel, JobStatus, type AssignedUserResponse } from '../../../api/generated/models';
import { SearchBar } from '../../../components/filters/SearchBar';
import { StatusFilter, getSavedStatusFilter, saveStatusFilter, announceSection } from '../../../components/filters/StatusFilter';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { useInfiniteList } from '../../../hooks/useInfiniteList';
import { useInfiniteScroll } from '../../../hooks/useInfiniteScroll';
import { useSearch } from '../../../hooks/useSearch';
import { apiClient } from '../../../lib/axios';
import { useAuth } from '../../../providers/useAuth';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';
import { formatJobStatus } from '../statusLabels';

const SCROLL_CONTAINER_SELECTOR = '.app-content';
const SCROLL_STORAGE_KEY = 'jobListScrollTop';
const PAGE_SIZE = 20;

const isReadonlyState = (status: JobStatus) =>
  status === JobStatus.InReview || status === JobStatus.Approved;

function getScrollContainer(): HTMLElement | null {
  return document.querySelector(SCROLL_CONTAINER_SELECTOR);
}

const SkeletonCard = () => (
  <div className="job-card job-card-skeleton" aria-hidden="true">
    <div className="job-card-header">
      <div className="skeleton skeleton-badge" />
      <div className="skeleton skeleton-id" />
    </div>
    <div className="skeleton skeleton-name" />
    <div className="skeleton skeleton-address" />
    <div className="job-card-footer">
      <div className="skeleton skeleton-tag" />
      <div className="skeleton skeleton-chevron" />
    </div>
  </div>
);

export const JobList = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const isAdmin = useIsAdmin();
  const [search, setSearch] = useState('');
  const [selectedStatuses, setSelectedStatuses] = useState<JobStatus[]>(() =>
    getSavedStatusFilter('mine-jobs', [JobStatus.Draft]),
  );

  const fetchStatuses = useMemo(
    () =>
      isAdmin
        ? [JobStatus.Draft, JobStatus.InReview, JobStatus.Approved, JobStatus.Rejected]
        : [JobStatus.Draft, JobStatus.InReview, JobStatus.Approved],
    [isAdmin],
  );

  const fetchJobsPage = useCallback(
    async ({ limit, offset }: { limit: number; offset: number }) =>
      (await apiClient.get('/api/jobs', {
        params: {
          status: fetchStatuses,
          limit,
          offset,
        },
      })) as JobListItemViewModel[],
    [fetchStatuses],
  );

  const query = useInfiniteList({
    queryKey: ['/api/jobs', { status: fetchStatuses, limit: PAGE_SIZE }],
    fetchPage: fetchJobsPage,
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

  const filtered = useMemo(() => {
    let result = query.items;

    if (!isAdmin) {
      const currentUserId = user?.id;
      if (!currentUserId) return [];
      result = result.filter((job) => job.assignedUsers.some((assignedUser) => assignedUser.id === currentUserId));
    }

    result = result.filter((job) => selectedStatuses.includes(job.status));

    return result;
  }, [query.items, isAdmin, user?.id, selectedStatuses]);

  const jobs = useSearch(filtered, search, (job, term) =>
    [
      job.customer?.name,
      job.customer?.address,
      job.customer?.email,
      job.customer?.contactPerson,
      job.customer?.phone,
      job.reportNumber,
      ...job.assignedUsers.map((assignedUser) => assignedUser.displayName),
    ].some((value) => value?.toLowerCase().includes(term)),
  );

  useEffect(() => {
    saveStatusFilter('mine-jobs', selectedStatuses);
  }, [selectedStatuses]);

  useEffect(() => {
    announceSection('mine-jobs');
  }, []);

  useEffect(() => {
    const handler = (e: PageTransitionEvent) => {
      if (e.persisted) {
        setSelectedStatuses(getSavedStatusFilter('mine-jobs', [JobStatus.Draft]));
      }
    };
    window.addEventListener('pageshow', handler);
    return () => window.removeEventListener('pageshow', handler);
  }, []);

  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
  }, [queryClient]);

  useEffect(() => {
    if (query.isLoading) return;
    const saved = sessionStorage.getItem(SCROLL_STORAGE_KEY);
    if (saved) {
      getScrollContainer()?.scrollTo({ top: Number(saved) });
    }
  }, [query.isLoading]);

  useEffect(() => {
    return () => {
      const top = getScrollContainer()?.scrollTop;
      if (top != null) {
        sessionStorage.setItem(SCROLL_STORAGE_KEY, String(top));
      }
    };
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
          <p>Kunne ikke hente jobs. Sørg for at du er logget ind.</p>
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
        <h2>Opgaver</h2>
        {isAdmin ? (
          <p className="subtitle">{jobs.length} registrerede opgaver</p>
        ) : (
          <p className="subtitle">Viser kun sager tildelt dig · {jobs.length} {jobs.length === 1 ? 'sag' : 'sager'}</p>
        )}
      </div>

      <StatusFilter
        options={
          isAdmin
            ? [
                { value: JobStatus.Draft, label: 'Aktiv' },
                { value: JobStatus.InReview, label: 'Til gennemsyn' },
                { value: JobStatus.Approved, label: 'Godkendt' },
                { value: JobStatus.Rejected, label: 'Afvist' },
              ]
            : [
                { value: JobStatus.Draft, label: 'Aktiv' },
                { value: JobStatus.InReview, label: 'Til gennemsyn' },
                { value: JobStatus.Approved, label: 'Godkendt' },
              ]
        }
        selected={selectedStatuses}
        onChange={setSelectedStatuses}
      />

      <SearchBar value={search} onChange={setSearch} placeholder="Søg opgaver..." />
      <div className="search-bar-spacer" />

      <div className="job-list">
        {jobs.map((job) => (
          <JobCard key={job.id} job={job} onOpen={() => navigate(isReadonlyState(job.status) ? `/app/completed/${job.id}` : `/app/job/${job.id}`, { state: { from: '/app' } })} />
        ))}

        {jobs.length === 0 && !query.isFetchingNextPage && (
          <div className="empty-state">
            <p>{isAdmin ? 'Du har ingen opgaver endnu.' : 'Du har ingen opgaver tildelt endnu.'}</p>
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

function JobCard({ job, onOpen }: { job: JobListItemViewModel; onOpen: () => void }) {
  return (
    <button className="job-card" onClick={onOpen} type="button">
      <div className="job-card-top">
        <div>
          <span className="job-number">SAG-{(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}</span>
          <h3 className="job-customer">{job.customer?.name || 'Ukendt kunde'}</h3>
        </div>
        <span className={`status-badge status-${job.status.toString().toLowerCase()}`}>
          {formatJobStatus(job.status)}
        </span>
      </div>

      <p className="job-address-row">
        <MapPin size={14} />
        <span className="job-address">{job.customer?.address || 'Ingen adresse angivet'}</span>
      </p>

      <div className="job-card-meta">
        <span className="meta-item">{formatInstallationTypes(job.installationTypes)}</span>
        {job.totalHours != null && (
          <span className="meta-item meta-hours">
            <Timer size={14} /> {job.totalHours} 
          </span>
        )}
      </div>

      <div className="job-card-footer">
        <AssignedUsers users={job.assignedUsers} />
        <span className="btn-icon" aria-label="Åbn sag">
          <ChevronRight size={20} />
        </span>
      </div>
    </button>
  );
}

function AssignedUsers({ users }: { users: AssignedUserResponse[] }) {
  if (users.length === 0) {
    return (
      <span className="unassigned">
        <User size={12} />
        <span>Ikke tildelt</span>
      </span>
    );
  }

  return (
    <div className="job-assigned">
      {users.slice(0, 2).map((user) => (
        <span key={user.id} className="assigned-user">
          <User size={12} />
          <span>{user.displayName}</span>
        </span>
      ))}
      {users.length > 2 && <span className="assigned-user">+{users.length - 2}</span>}
    </div>
  );
}

function formatInstallationTypes(installationTypes: string[]) {
  if (installationTypes.length === 0) return 'Ingen installationstype';
  if (installationTypes.length === 1) return installationTypes[0];
  return `${installationTypes[0]} +${installationTypes.length - 1}`;
}
