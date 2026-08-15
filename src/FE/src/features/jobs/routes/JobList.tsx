import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronRight, Clock, MapPin, RefreshCw, Timer, User } from 'lucide-react';
import { type JobListItemViewModel, JobStatus, type AssignedUserResponse } from '../../../api/generated/models';
import { formatJobType } from '../statusLabels';
import { CopyAddressButton } from '../../../components/CopyAddressButton';
import { SearchBar } from '../../../components/filters/SearchBar';
import { StatusFilter, getSavedStatusFilter, saveStatusFilter, announceSection } from '../../../components/filters/StatusFilter';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { usePaginatedList } from '../../../hooks/usePaginatedList';
import { usePullToRefresh } from '../../../hooks/usePullToRefresh';
import { useColumnResize } from '../../../hooks/useColumnResize';
import { apiClient } from '../../../lib/axios';
import { useAuth } from '../../../providers/useAuth';
import { ErrorState } from '../../../components/ErrorState';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';
import { formatDateLong, formatDateTimeShort } from '../../../lib/formatDate';
import { formatJobStatus } from '../statusLabels';
import '../components/RejectedJobsIndicator.css';


const PAGE_SIZE = 20;

const isReadonlyState = (status: JobStatus) =>
  status === JobStatus.InReview || status === JobStatus.Approved;

const shouldShowReviewDot = (status: JobStatus) =>
  status === JobStatus.InReview;

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
  const { user } = useAuth();
  const isAdmin = useIsAdmin();
  const [selectedStatuses, setSelectedStatuses] = useState<JobStatus[]>(() =>
    getSavedStatusFilter('mine-jobs', [JobStatus.Draft]),
  );
  const { handleMouseDown } = useColumnResize();
  const sortScrollRef = useRef<HTMLDivElement>(null);
  const [sortCanScrollLeft, setSortCanScrollLeft] = useState(false);
  const [sortCanScrollRight, setSortCanScrollRight] = useState(false);

  const updateSortScrollState = useCallback(() => {
    const el = sortScrollRef.current;
    if (!el) return;
    const { scrollLeft, scrollWidth, clientWidth } = el;
    setSortCanScrollLeft(scrollLeft > 4);
    setSortCanScrollRight(scrollLeft + clientWidth < scrollWidth - 4);
  }, []);

  useEffect(() => {
    const el = sortScrollRef.current;
    if (!el) return;
    updateSortScrollState();
    el.addEventListener('scroll', updateSortScrollState, { passive: true });
    const observer = new ResizeObserver(updateSortScrollState);
    observer.observe(el);
    return () => {
      el.removeEventListener('scroll', updateSortScrollState);
      observer.disconnect();
    };
  }, [updateSortScrollState]);

  const handleStatusChange = useCallback((statuses: JobStatus[]) => {
    setSelectedStatuses(statuses);
    saveStatusFilter('mine-jobs', statuses);
  }, []);

  const fetchJobsPage = useCallback(
    async ({ limit, offset, search, sortBy, sortDirection }: { limit: number; offset: number; search?: string; sortBy?: string; sortDirection?: string }) => {
      const data = await apiClient.get('/api/jobs', {
        params: {
          status: selectedStatuses,
          search: search || undefined,
          sortBy: sortBy || undefined,
          sortDirection: sortDirection || undefined,
          limit,
          offset,
        },
      }) as { items: JobListItemViewModel[]; totalCount: number };
      return data;
    },
    [selectedStatuses],
  );

  const {
    items,
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
    pageStart,
    pageEnd,
    sentinelRef,
    isDesktop,
  } = usePaginatedList<JobListItemViewModel>({
    queryKey: ['/api/jobs', { status: selectedStatuses }],
    fetchPage: fetchJobsPage,
    pageSize: PAGE_SIZE,
    storageKey: 'jobs',
  });

  const { pullDistance, isRefreshing, willRefresh } = usePullToRefresh({
    enabled: !isDesktop,
    onRefresh: refetch,
  });
  const showPullIndicator = isRefreshing || pullDistance > 0;
  const pullIndicatorHeight = isRefreshing ? 44 : pullDistance;
  const pullInstruction = isRefreshing
    ? 'Opdaterer opgaver…'
    : willRefresh
      ? 'Slip for at opdatere'
      : 'Træk ned for at opdatere';

  const displayedJobs = useMemo(() => {
    let result = items;
    if (!isAdmin) {
      const currentUserId = user?.id;
      if (!currentUserId) return [];
      result = result.filter((job) => job.assignedUsers.some((au) => au.id === currentUserId));
    }
    return result;
  }, [items, isAdmin, user?.id]);

  const desktopPageItems = isDesktop ? displayedJobs.slice(pageStart, pageEnd) : displayedJobs;
  const isPageDataLoaded = items.length >= pageEnd;
  const showPageLoading = isDesktop && isFetchingNextPage && !isPageDataLoaded;

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

  const showLoadingSkeleton = isLoading && items.length === 0;

  const isErrored = isError && items.length === 0;

  return (
    <div className="page-container">
      <div
        className={`pull-to-refresh${isRefreshing ? ' refreshing' : ''}${willRefresh ? ' ready' : ''}`}
        style={{ height: `${pullIndicatorHeight}px` }}
        role="status"
        aria-live="polite"
        aria-hidden={!showPullIndicator}
      >
        <RefreshCw size={16} className={isRefreshing ? 'animate-spin' : undefined} />
        <span>{pullInstruction}</span>
      </div>
      {isFetching && <div className="data-table-loading-bar" />}
      <div className="page-header">
        {showLoadingSkeleton ? (
          <div className="skeleton skeleton-title" />
        ) : (
          <h2>Opgaver</h2>
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
                { value: JobStatus.Rejected, label: 'Afvist' },
              ]
        }
        selected={selectedStatuses}
        onChange={handleStatusChange}
      />
      <SearchBar value={search} onChange={handleSearchChange} placeholder="Søg opgaver..." />

      {isErrored ? (
        <ErrorState message="Kunne ikke hente jobs. Sørg for at du er logget ind." onRetry={() => void refetch()} />
      ) : showLoadingSkeleton ? (
        isDesktop ? (
          <>
          <table className="data-table">
            <thead>
              <tr>
                <th className="col-number">Sagsnr.</th>
                <th className="col-type">Type</th>
                <th className="col-name">Kunde</th>
                <th className="col-address">Adresse</th>
                <th className="col-status">Status</th>
                <th className="col-installation">Anlæg</th>
                <th className="col-hours">Timer</th>
                <th className="col-users">Medarbejdere</th>
                <th className="col-date">Rapp. dato</th>
                <th className="col-date">Opdateret</th>
                <th className="col-actions" />
              </tr>
            </thead>
            <tbody>
              {Array.from({ length: 5 }).map((_, i) => (
                <tr key={i}>
                  {Array.from({ length: 11 }).map((_, j) => (
                    <td key={j}><div className="skeleton" style={{ height: '1em', width: j === 10 ? '1.5rem' : '80%' }} /></td>
                  ))}
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
              <th className={`col-number sortable${sortBy === 'reportNumber' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('reportNumber')}>
                  Sagsnr.<span className="sort-icon">{sortBy === 'reportNumber' ? (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(0, e)} />
              </th>
              <th className="col-type">
                Type
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(1, e)} />
              </th>
              <th className={`col-name sortable${sortBy === 'name' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('name')}>
                  Kunde<span className="sort-icon">{sortBy === 'name' ? (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(2, e)} />
              </th>
              <th className={`col-address sortable${sortBy === 'address' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('address')}>
                  Adresse<span className="sort-icon">{sortBy === 'address' ? (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(3, e)} />
              </th>
              <th className="col-installation">
                Anlæg
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(4, e)} />
              </th>
              <th className={`col-hours sortable${sortBy === 'totalHours' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('totalHours')}>
                  Timer<span className="sort-icon">{sortBy === 'totalHours' ? (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(5, e)} />
              </th>
              <th className="col-users">
                Medarbejdere
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(6, e)} />
              </th>
              <th className="col-status">
                Status
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(7, e)} />
              </th>
              <th className={`col-date sortable${sortBy === 'reportDate' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('reportDate')}>
                  Rapp. dato<span className="sort-icon">{sortBy === 'reportDate' ? (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(8, e)} />
              </th>
              <th className={`col-date sortable${sortBy === 'updatedAt' ? ' sorted' : ''}`}>
                <button type="button" className="sort-trigger" onClick={() => handleSort('updatedAt')}>
                  Opdateret<span className="sort-icon">{sortBy === 'updatedAt' ? (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />) : <ArrowUpDown size={14} />}</span>
                </button>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(9, e)} />
              </th>
              <th className="col-actions">
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(10, e)} />
              </th>
            </tr>
          </thead>
          <tbody>
            {showPageLoading ? (
              Array.from({ length: 5 }).map((_, i) => (
                <tr key={`skeleton-${i}`}>
                  {Array.from({ length: 11 }).map((_, j) => (
                    <td key={j}><div className="skeleton" style={{ height: '1em', width: j === 10 ? '1.5rem' : '80%' }} /></td>
                  ))}
                </tr>
              ))
            ) : (
              desktopPageItems.map((job) => {
                const address = job.destinationAddress || job.customer?.address;
                const openJob = () => navigate(
                  isReadonlyState(job.status) ? `/app/completed/${job.id}` : `/app/job/${job.id}`,
                  { state: { from: '/app' } },
                );
                return (
              <tr
                key={job.id}
                className={`clickable${job.status === JobStatus.Rejected ? ' job-row--rejected' : ''}`}
                onClick={openJob}
                onKeyDown={(event) => {
                  if (event.target !== event.currentTarget) return;
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    openJob();
                  }
                }}
                tabIndex={0}
              >
                <td>
                  <span className="job-number">SAG-{(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}</span>
                  {shouldShowReviewDot(job.status) && <span className="review-dot" aria-hidden="true" />}
                  {job.status === JobStatus.Approved && <span className="approved-dot" aria-hidden="true" />}
                  {!job.isSeen && <span className="unread-dot" role="img" aria-label="Ulæst" />}
                  {job.isNewRejection && <span className="rejected-dot" role="img" aria-label="Ny afvisning" />}
                  {isAdmin && job.assignedUsers.length === 0 && <span className="unassigned-dot" role="img" aria-label="Ikke tildelt" />}
                </td>
                <td><span className={`job-type-badge job-type-${job.jobType?.toLowerCase()}`}>{formatJobType(job.jobType)}</span></td>
                <td>{job.customer?.name || job.taskDescription}</td>
                <td>
                  <span>{address}</span>
                  <CopyAddressButton address={address} />
                </td>
                <td>
                  <InstallationTypeTags types={job.installationTypes} />
                </td>
                <td className="cell-number"> {job.totalHours}</td>
                <td>
                  <TableAssignedUsers users={job.assignedUsers} />
                </td>
                <td>
                  <span className={`status-badge-cell cell-status-${job.status}`}>
                    {formatJobStatus(job.status)}
                  </span>
                </td>
                <td className="cell-date">{formatDateLong(job.reportDate)}</td>
                <td className="cell-date">{formatDateLong(job.updatedAt)}</td>
                <td className="col-actions">
                  <ChevronRight size={16} className="row-link-icon" aria-hidden="true" />
                </td>
              </tr>
                );
              }))}
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
        <div
          className="job-sort-controls-scroll"
          data-scroll-left={sortCanScrollLeft}
          data-scroll-right={sortCanScrollRight}
        >
        <div className="job-sort-controls" ref={sortScrollRef}>
          <button
            type="button"
            className={`sort-btn${sortBy === 'reportNumber' ? ' active' : ''}`}
            onClick={() => handleSort('reportNumber')}
          >
            Sagsnr.{sortBy === 'reportNumber' && (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />)}
          </button>
          <button
            type="button"
            className={`sort-btn${sortBy === 'name' ? ' active' : ''}`}
            onClick={() => handleSort('name')}
          >
            Kundenavn{sortBy === 'name' && (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />)}
          </button>
          <button
            type="button"
            className={`sort-btn${sortBy === 'address' ? ' active' : ''}`}
            onClick={() => handleSort('address')}
          >
            Adresse{sortBy === 'address' && (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />)}
          </button>
          <div className="job-sort-secondary">
            <button
              type="button"
              className={`sort-btn${sortBy === 'updatedAt' ? ' active' : ''}`}
              onClick={() => handleSort('updatedAt')}
            >
              Opdateret{sortBy === 'updatedAt' && (sortDirection === 'asc' ? <ArrowUp size={10} /> : <ArrowDown size={10} />)}
            </button>
          </div>
        </div>
        </div>
      )}

      <div className="job-list">
        {!isDesktop && desktopPageItems.map((job) => (
          <JobCard key={job.id} job={job} isAdmin={isAdmin} onOpen={() => navigate(isReadonlyState(job.status) ? `/app/completed/${job.id}` : `/app/job/${job.id}`, { state: { from: '/app' } })} />
        ))}

        {displayedJobs.length === 0 && !isFetchingNextPage && (
          <div className="empty-state">
            <p>{isAdmin ? 'Du har ingen opgaver endnu.' : 'Du har ingen opgaver tildelt endnu.'}</p>
          </div>
        )}

        {!isDesktop && (
          <InfiniteScrollSentinel
            sentinelRef={sentinelRef}
            isLoading={isFetchingNextPage}
          />
        )}
      </div>
      </>
      )}
    </div>
  );
};

export function JobCard({ job, onOpen, isAdmin }: { job: JobListItemViewModel; onOpen: () => void; isAdmin: boolean }) {
  const address = job.destinationAddress || job.customer?.address;
  return (
    <div
      className={`job-card${job.status === JobStatus.Rejected ? ' job-card--rejected' : ''}`}
      onClick={onOpen}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) return;
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          onOpen();
        }
      }}
      role="link"
      tabIndex={0}
    >
      <div className="job-card-top">
        <div>
          <span className="job-number">SAG-{(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}<span className="job-number-sep">&middot;</span>{formatJobType(job.jobType)}<span className="job-number-sep">&middot;</span><span className="job-number-status">{formatJobStatus(job.status)}</span></span>
          {shouldShowReviewDot(job.status) && <span className="review-dot" aria-hidden="true" />}
          {job.status === JobStatus.Approved && <span className="approved-dot" aria-hidden="true" />}
          {!job.isSeen && <span className="unread-dot" role="img" aria-label="Ulæst" />}
          {job.isNewRejection && <span className="rejected-dot" role="img" aria-label="Ny afvisning" />}
          {isAdmin && job.assignedUsers.length === 0 && <span className="unassigned-dot" role="img" aria-label="Ikke tildelt" />}
          <h3 className="job-customer">{job.customer?.name || job.taskDescription}</h3>
        </div>
      </div>

      <p className="job-address-row">
        <MapPin size={14} aria-hidden="true" />
        <span className="job-address">{address || 'Ingen adresse angivet'}</span>
        <CopyAddressButton address={address} />
      </p>

      <div className="job-card-meta">
        <span className="meta-item"><InstallationTypeTags types={job.installationTypes} /></span>
        {job.totalHours != null && (
          <span className="meta-item meta-hours">
            <Timer size={14} aria-hidden="true" /> {job.totalHours}
          </span>
        )}
        <span className="meta-item meta-updated">
          <Clock size={14} aria-hidden="true" /> Opdateret {formatDateTimeShort(job.updatedAt)}
        </span>
      </div>

      <div className="job-card-footer">
        <AssignedUsers users={job.assignedUsers} />
        <span className="btn-icon" aria-hidden="true">
          <ChevronRight size={20} />
        </span>
      </div>
    </div>
  );
}

function AssignedUsers({ users }: { users: AssignedUserResponse[] }) {
  if (users.length === 0) {
    return (
      <span className="unassigned">
        <User size={12} aria-hidden="true" />
        <span>Ikke tildelt</span>
      </span>
    );
  }

  return (
    <span className="cell-comma-list">
      {users.map((user) => (
        <span key={user.id} className="cell-comma-list-item">{user.displayName}</span>
      ))}
    </span>
  );
}

function TableAssignedUsers({ users }: { users: AssignedUserResponse[] }) {
  return (
    <span className="cell-comma-list">
      {users.map((user) => (
        <span key={user.id} className="cell-comma-list-item">{user.displayName}</span>
      ))}
    </span>
  );
}

function InstallationTypeTags({ types }: { types: string[] }) {
  return (
    <span className="cell-comma-list">
      {types.map((type) => (
        <span key={type} className="cell-comma-list-item">{type}</span>
      ))}
    </span>
  );
}
