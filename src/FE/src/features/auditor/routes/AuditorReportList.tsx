import { useCallback, useEffect, useLayoutEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronRight, MapPin, Timer, User } from 'lucide-react';
import type { JobListItemViewModel } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { ErrorState } from '../../../components/ErrorState';
import { CopyAddressButton } from '../../../components/CopyAddressButton';
import { SearchBar } from '../../../components/filters/SearchBar';
import { StatusFilter, saveStatusFilter, announceSection } from '../../../components/filters/StatusFilter';
import { InfiniteScrollSentinel } from '../../../components/pagination/InfiniteScrollSentinel';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { useInfiniteList } from '../../../hooks/useInfiniteList';
import { useInfiniteScroll } from '../../../hooks/useInfiniteScroll';
import { useColumnResize } from '../../../hooks/useColumnResize';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
import { useSearch } from '../../../hooks/useSearch';
import { apiClient } from '../../../lib/axios';
import { formatDateLong } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';
import { useAppScrollRestoreKey } from '../../../hooks/useAppRouteScroll';

const SCROLL_CONTAINER_SELECTOR = '.app-shell';
const SCROLL_STORAGE_KEY = 'auditorReportListScrollTop';
const PAGE_SIZE = 20;
const COMPLETED_STATUSES = [JobStatus.Approved] as const;

function getScrollContainer(): HTMLElement | null {
  return document.querySelector(SCROLL_CONTAINER_SELECTOR);
}

function getSavedAuditorStatusFilter(): JobStatus[] {
  try {
    const saved = sessionStorage.getItem('statusFilter:auditor-reports');
    if (saved) {
      const parsed = JSON.parse(saved);
      if (Array.isArray(parsed) && parsed.length > 0) {
        return parsed as JobStatus[];
      }
    }
  } catch {
    // ignore parse errors
  }
  return [JobStatus.Approved];
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

export const AuditorReportList = () => {
  const navigate = useNavigate();
  const restoreScrollKey = useAppScrollRestoreKey();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [selectedStatuses, setSelectedStatuses] = useState<JobStatus[]>(() => getSavedAuditorStatusFilter());
  const [sortBy, setSortBy] = useState('');
  const [sortDirection, setSortDirection] = useState('asc');
  const [viewPage, setViewPage] = useState(1);
  const isDesktop = useMediaQuery('(min-width: 768px)');
  const { handleMouseDown } = useColumnResize();

  const handleSort = (field: string) => {
    if (sortBy === field) {
      setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(field);
      setSortDirection('asc');
    }
  };

  const fetchStatuses = useMemo(() => [...COMPLETED_STATUSES], []);

  const fetchJobsPage = useCallback(
    async ({ limit, offset }: { limit: number; offset: number }) => {
      const data = await apiClient.get('/api/jobs', {
        params: {
          status: fetchStatuses,
          limit,
          offset,
        },
      }) as { items: JobListItemViewModel[]; totalCount: number };
      return data;
    },
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
    result = result.filter((job) => selectedStatuses.includes(job.status));
    return result;
  }, [query.items, selectedStatuses]);

  const searched = useSearch(filtered, search, (job, term) =>
    [
      job.customer?.name,
      job.customer?.address,
      job.customer?.email,
      job.customer?.contactPerson,
      job.customer?.phone,
      job.reportNumber,
      ...job.assignedUsers.map((u) => u.displayName),
    ].some((value) => value?.toLowerCase().includes(term)),
  );

  const jobs = useMemo(() => {
    if (!sortBy) return searched;
    return [...searched].sort((a, b) => {
      let cmp = 0;
      switch (sortBy) {
        case 'reportNumber':
          cmp = (a.reportNumber || '').localeCompare(b.reportNumber || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'name':
          cmp = (a.customer?.name || '').localeCompare(b.customer?.name || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'address':
          cmp = (a.customer?.address || '').localeCompare(b.customer?.address || '', 'da-DK', { sensitivity: 'base' });
          break;
        case 'totalHours':
          cmp = (a.totalHours != null ? Number(a.totalHours) : -1) - (b.totalHours != null ? Number(b.totalHours) : -1);
          break;
        case 'reportDate':
          cmp = (a.reportDate || '').localeCompare(b.reportDate || '');
          break;
        case 'updatedAt':
          cmp = (a.updatedAt || '').localeCompare(b.updatedAt || '');
          break;
      }
      return sortDirection === 'asc' ? cmp : -cmp;
    });
  }, [searched, sortBy, sortDirection]);

  const totalPages = Math.max(1, Math.ceil(jobs.length / PAGE_SIZE));
  const safeViewPage = Math.min(viewPage, totalPages);
  const pageStart = (safeViewPage - 1) * PAGE_SIZE;
  const pageEnd = pageStart + PAGE_SIZE;
  const displayedJobs = isDesktop ? jobs.slice(pageStart, pageEnd) : jobs;

  useEffect(() => {
    setViewPage(1);
  }, [search, sortBy, sortDirection]);

  useEffect(() => { saveStatusFilter('auditor-reports', selectedStatuses); }, [selectedStatuses]);
  useEffect(() => { announceSection('auditor-reports'); }, []);
  useEffect(() => { void queryClient.invalidateQueries({ queryKey: ['/api/jobs'] }); }, [queryClient]);
  useEffect(() => {
    if (query.isLoading || !restoreScrollKey) return;
    const saved = sessionStorage.getItem(SCROLL_STORAGE_KEY);
    if (!saved) return;

    const scrollTop = Number(saved);
    if (!Number.isFinite(scrollTop) || scrollTop < 0) return;
    getScrollContainer()?.scrollTo({ top: scrollTop });
  }, [query.isLoading, restoreScrollKey]);
  useLayoutEffect(() => {
    return () => {
      const top = getScrollContainer()?.scrollTop;
      if (top != null) sessionStorage.setItem(SCROLL_STORAGE_KEY, String(top));
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
        <ErrorState message="Kunne ikke hente rapporter. Sørg for at du er logget ind." onRetry={() => void query.refetch()} />
      </div>
    );
  }

  const statusOptions = [
    { value: JobStatus.Approved, label: 'Godkendt' },
  ];

  return (
    <div className="page-container">
      <div className="page-header">
        <h1>Rapporter</h1>
      </div>
      <StatusFilter options={statusOptions} selected={selectedStatuses} onChange={setSelectedStatuses} />
      <div className="search-row">
        <SearchBar value={search} onChange={setSearch} placeholder="Søg rapporter..." />
      </div>

      {isDesktop ? (
        <>
        <table className="data-table">
          <thead>
            <tr>
              <th className={`col-number sortable${sortBy === 'reportNumber' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('reportNumber')}>
                  Sagsnr.<span className="sort-icon">{sortBy === 'reportNumber' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(0, e)} />
              </th>
              <th className={`col-name sortable${sortBy === 'name' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('name')}>
                  Kunde<span className="sort-icon">{sortBy === 'name' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(1, e)} />
              </th>
              <th className={`col-address sortable${sortBy === 'address' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('address')}>
                  Adresse<span className="sort-icon">{sortBy === 'address' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(2, e)} />
              </th>
              <th className="col-status">
                Status
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(3, e)} />
              </th>
              <th className="col-installation">
                Anlæg
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(4, e)} />
              </th>
              <th className={`col-hours sortable${sortBy === 'totalHours' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('totalHours')}>
                  Timer<span className="sort-icon">{sortBy === 'totalHours' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(5, e)} />
              </th>
              <th className="col-users">
                Medarbejdere
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(6, e)} />
              </th>
              <th className={`col-date sortable${sortBy === 'reportDate' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('reportDate')}>
                  Rapp. dato<span className="sort-icon">{sortBy === 'reportDate' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(7, e)} />
              </th>
              <th className={`col-date sortable${sortBy === 'updatedAt' ? ' sorted' : ''}`}>
                <span className="sort-trigger" onClick={() => handleSort('updatedAt')}>
                  Opdateret<span className="sort-icon">{sortBy === 'updatedAt' ? (sortDirection === 'asc' ? <ArrowUp size={14} /> : <ArrowDown size={14} />) : <ArrowUpDown size={14} />}</span>
                </span>
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(8, e)} />
              </th>
              <th className="col-actions">
                <div className="col-resize-handle" onMouseDown={(e) => handleMouseDown(9, e)} />
              </th>
            </tr>
          </thead>
          <tbody>
            {displayedJobs.map((job) => (
              <tr
                key={job.id}
                className="clickable"
                onClick={() => navigate(`/app/completed/${job.id}`, { state: { from: '/app/auditor', readOnly: true } })}
              >
                <td><span className="job-number">SAG-{(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}</span></td>
                <td>{job.customer?.name || 'Ukendt kunde'}</td>
                <td>
                  <span>{job.customer?.address || '—'}</span>
                  <CopyAddressButton address={job.customer?.address} />
                </td>
                <td>
                  <span className={`status-badge-cell cell-status-${job.status}`}>
                    {formatJobStatus(job.status)}
                  </span>
                </td>
                <td><InstallationTypeTags types={job.installationTypes} /></td>
                <td className="cell-number">{job.totalHours != null ? `${job.totalHours}` : '—'}</td>
                <td>
                  <AuditorTableAssignedUsers users={job.assignedUsers} />
                </td>
                <td className="cell-date">{formatDateLong(job.reportDate) ?? '—'}</td>
                <td className="cell-date">{formatDateLong(job.updatedAt) ?? '—'}</td>
                <td className="col-actions">
                  <ChevronRight size={16} className="row-link-icon" />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <PaginationControls
          page={safeViewPage}
          totalCount={jobs.length}
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
          {jobs.map((job) => (
            <ReportCard
              key={job.id}
              job={job}
              onOpen={() => navigate(`/app/completed/${job.id}`, { state: { from: '/app/auditor', readOnly: true } })}
            />
          ))}
          {jobs.length === 0 && !query.isFetchingNextPage && (
            <div className="empty-state">
              <p>Ingen afsluttede rapporter</p>
            </div>
          )}
          {!isDesktop && (
            <InfiniteScrollSentinel sentinelRef={sentinelRef} isLoading={query.isFetchingNextPage} />
          )}
        </div>
      )}
    </div>
  );
};

function ReportCard({ job, onOpen }: { job: JobListItemViewModel; onOpen: () => void }) {
  const address = job.customer?.address;
  return (
    <div
      className="job-card"
      onClick={onOpen}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) return;
        if (event.key === 'Enter' || event.key === ' ') onOpen();
      }}
      role="link"
      tabIndex={0}
    >
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
        <span className="job-address">{address || 'Ingen adresse angivet'}</span>
        <CopyAddressButton address={address} />
      </p>
      <div className="job-card-meta">
        <span className="meta-item"><InstallationTypeTags types={job.installationTypes} /></span>
        {job.totalHours != null && (
          <span className="meta-item meta-hours">
            <Timer size={14} /> {job.totalHours} timer
          </span>
        )}
      </div>
      <div className="job-card-footer">
        <AssignedUsers users={job.assignedUsers} />
        <span className="btn-icon" aria-label="Åbn rapport">
          <ChevronRight size={20} />
        </span>
      </div>
    </div>
  );
}

function AssignedUsers({ users }: { users: JobListItemViewModel['assignedUsers'] }) {
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

function InstallationTypeTags({ types }: { types: string[] }) {
  if (types.length === 0) return <span className="text-muted">—</span>;
  return (
    <span className="cell-comma-list">
      {types.map((type) => (
        <span key={type} className="cell-comma-list-item">{type}</span>
      ))}
    </span>
  );
}

function AuditorTableAssignedUsers({ users }: { users: JobListItemViewModel['assignedUsers'] }) {
  if (users.length === 0) {
    return <span className="text-muted" style={{ fontSize: 'var(--fs-xs)' }}>—</span>;
  }

  return (
    <span className="cell-comma-list">
      {users.map((user) => (
        <span key={user.id} className="cell-comma-list-item">{user.displayName}</span>
      ))}
    </span>
  );
}
