import { useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ErrorState } from '../../../components/ErrorState';
import { useQueryClient } from '@tanstack/react-query';
import { notify } from '../../../lib/toast';
import { useScrollRestore } from '../../../hooks/useScrollRestore';
import {
  ArrowLeft,
  Building2,
  ChevronRight,
  Clock,
  ClipboardList,
  Loader2,
  Mail,
  MapPin,
  Search,
  Shield,
  Timer,
  UserMinus,
  UserPlus,
} from 'lucide-react';
import { useGetApiUsersId, getGetApiUsersIdQueryKey } from '../../../api/generated/users/users';
import { useGetApiJobs, usePostApiJobsIdAssign } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models';
import { formatDateLong } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { announceSection } from '../../../components/filters/StatusFilter';
import { canReceiveJobAssignment } from '../../../providers/permissions';

function formatHours(value: number | string | null): string {
  if (value == null) return '\u2013';
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (Number.isNaN(num)) return '\u2013';
  return `${num.toLocaleString('da-DK', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} t`;
}


type SearchResult = {
  id: string;
  reportNumber: string | null;
  status: string;
  softDeleted: boolean;
  customer: {
    name: string | null;
    address?: string | null;
  } | null;
  customerName?: string | null;
  customerEmail?: string | null;
  destinationAddress?: string | null;
  assignedUsers: { id: string; displayName: string }[];
  updatedAt?: string;
};

// Statuses we surface for assignment. Completed / rejected / approved
// jobs can be re-opened but it's not a normal "tildel" flow — keep the
// suggestion pool focused on work that's still active.
const ASSIGNABLE_STATUSES = [JobStatus.Draft, JobStatus.InReview] as const;
const SUGGESTION_LIMIT = 5;
const SEARCH_LIMIT = 30;

function formatJobNumber(reportNumber: string | null, id: string) {
  const prefix = reportNumber || id.slice(0, 4);
  return `Sag-${prefix}`;
}

export const UserDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const query = useGetApiUsersId(id!);
  const user = query.data;

  useScrollRestore(`user:${id}`);

  const [searchValue, setSearchValue] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [assigningJobId, setAssigningJobId] = useState<string | null>(null);

  useEffect(() => {
    announceSection('users');
  }, []);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchValue), 300);
    return () => clearTimeout(timer);
  }, [searchValue]);

  // Two parallel searches when the user types: one by report number,
  // one by customer name. We merge + de-dupe by id. Both endpoints
  // already exist on /api/jobs — no backend changes.
  const searchQuery = useGetApiJobs(
    debouncedSearch.length >= 2
      ? { reportNumber: debouncedSearch, status: [...ASSIGNABLE_STATUSES], sortBy: 'reportNumber', sortDirection: 'asc', limit: SEARCH_LIMIT }
      : undefined,
    {
      query: {
        enabled: debouncedSearch.length >= 2,
      },
    },
  );

  const customerSearchQuery = useGetApiJobs(
    debouncedSearch.length >= 2
      ? { customerName: debouncedSearch, status: [...ASSIGNABLE_STATUSES], sortBy: 'reportNumber', sortDirection: 'asc', limit: SEARCH_LIMIT }
      : undefined,
    {
      query: {
        enabled: debouncedSearch.length >= 2,
      },
    },
  );

  const searchResults: SearchResult[] = useMemo(() => {
    const a = (searchQuery.data) as unknown as { items?: SearchResult[] } | undefined;
    const b = (customerSearchQuery.data) as unknown as { items?: SearchResult[] } | undefined;
    const arrA = a?.items ?? [];
    const arrB = b?.items ?? [];
    const seen = new Set<string>();
    const merged: SearchResult[] = [];
    for (const job of [...arrA, ...arrB]) {
      if (seen.has(job.id)) continue;
      seen.add(job.id);
      merged.push(job);
    }
    return merged;
  }, [searchQuery.data, customerSearchQuery.data]);

  const isSearching = debouncedSearch.length >= 2;

  // Suggestions: a small pool of recently updated open jobs, surfaced
  // when the search box is empty so the user doesn't have to remember
  // a job number just to assign work.
  const suggestionsQuery = useGetApiJobs(
    { status: [...ASSIGNABLE_STATUSES], sortBy: 'reportNumber', sortDirection: 'asc', limit: SUGGESTION_LIMIT },
    {
      query: {
        enabled: !isSearching,
      },
    },
  );

  const suggestionResults: SearchResult[] = useMemo(() => {
    const raw = (suggestionsQuery.data) as unknown as { items?: SearchResult[] } | undefined;
    return raw?.items ?? [];
  }, [suggestionsQuery.data]);

  const isRemovingRef = useRef(false);

  const assignMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: getGetApiUsersIdQueryKey(id!) });
        queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
        const name = user?.displayName ?? 'Brugeren';
        const wasRemoving = isRemovingRef.current;
        isRemovingRef.current = false;
        notify.success(wasRemoving ? `${name} er fjernet fra sagen` : `${name} er tilknyttet sagen`);
        setAssigningJobId(null);
      },
      onError: () => {
        isRemovingRef.current = false;
        notify.error('Handlingen kunne ikke udføres');
        setAssigningJobId(null);
      },
    },
  });

  const handleAssign = (job: SearchResult) => {
    if (assigningJobId) return;
    if (job.softDeleted) {
      notify.error('Sagen er slettet og kan ikke tildeles');
      return;
    }

    const currentUserIds = job.assignedUsers.map((u) => u.id);
    const alreadyAssigned = currentUserIds.includes(id!);

    setAssigningJobId(job.id);
    isRemovingRef.current = alreadyAssigned;
    assignMutation.mutate({
      id: job.id,
      data: {
        userIds: alreadyAssigned
          ? currentUserIds.filter((uid) => uid !== id!)
          : [...currentUserIds, id!],
      },
    });
  };

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="detail-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
      </div>
    );
  }

  if (query.isError || !user) {
    return (
      <div className="page-container">
        <ErrorState message="Kunne ikke hente brugeroplysninger.">
          <button className="btn btn-primary" onClick={() => navigate('/app/users')}>
            Tilbage til brugerliste
          </button>
        </ErrorState>
      </div>
    );
  }

  const canReceiveJobs = canReceiveJobAssignment(user.role);

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/users')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <h2>{user.displayName}</h2>
      </div>

      <div className="user-detail-info">
        <div className="detail-row">
          <Mail size={16} />
          <span>{user.email}</span>
        </div>
        <div className="detail-row">
          <Shield size={16} />
          <span>{user.role}</span>
        </div>
        <div className="detail-row">
          <Clock size={16} />
          <span>{formatHours(user.hoursThisWeek)} denne uge</span>
        </div>
        <div className="detail-row">
          <Clock size={16} />
          <span>{formatHours(user.hoursBiweekly)} / 14 dage</span>
        </div>
        <div className="detail-row">
          <Clock size={16} />
          <span>{formatHours(user.hoursThisMonth)} denne måned</span>
        </div>
        <div className="detail-row">
          <Timer size={16} />
          <span>{formatHours(user.totalHours)} i alt</span>
        </div>
      </div>

      {canReceiveJobs ? (
        <>
          <h3 className="section-title">Tildel sag</h3>

      <div className="search-input-wrapper">
        <Search size={16} className="search-input-icon" />
        <input
          type="text"
          className="search-input"
          placeholder="Søg på sagsnummer eller kundenavn..."
          value={searchValue}
          onChange={(e) => setSearchValue(e.target.value)}
        />
      </div>

      {isSearching && (
        <div className="job-list">
          {(searchQuery.isLoading || customerSearchQuery.isLoading) && (
            <div className="empty-state">
              <p>Søger...</p>
            </div>
          )}

          {(searchQuery.isError || customerSearchQuery.isError) && (
            <ErrorState message="Kunne ikke søge efter sager." />
          )}

          {!searchQuery.isLoading && !customerSearchQuery.isLoading &&
            !searchQuery.isError && !customerSearchQuery.isError &&
            searchResults.length === 0 && (
            <div className="empty-state">
              <p>Ingen sager fundet.</p>
            </div>
          )}

          {searchResults.map((job) => renderAssignableJobCard(job))}
        </div>
      )}

          {!isSearching && (
        <div className="job-list">
          {suggestionsQuery.isLoading && (
            <div className="empty-state">
              <p>Henter forslag...</p>
            </div>
          )}

          {suggestionsQuery.isError && (
            <ErrorState message="Kunne ikke hente forslag." />
          )}

          {!suggestionsQuery.isLoading && !suggestionsQuery.isError && suggestionResults.length === 0 && (
            <div className="empty-state">
              <p>Ingen åbne sager at foreslå.</p>
            </div>
          )}

          {suggestionResults.map((job) => renderAssignableJobCard(job))}
        </div>
          )}
        </>
      ) : (
        <div className="empty-state">
          <p>Auditorer og superadmins kan ikke tildeles sager.</p>
        </div>
      )}

      <CollapsibleSection
        icon={<ClipboardList size={18} />}
        title={`Tildelte opgaver (${user.assignedJobs.length})`}
        defaultOpen={false}
        className="assigned-jobs-section"
      >
        {user.assignedJobs.map((job) => (
          <button
            key={job.reportId}
            className="job-card"
            onClick={() => navigate(`/app/completed/${job.reportId}`, { state: { from: `/app/users/${id}` } })}
            type="button"
          >
            <div className="job-card-top">
              <div>
                <span className="job-number">
                  {formatJobNumber(job.reportNumber, job.reportId)}
                </span>
              </div>
              <span className={`status-badge status-${job.status.toLowerCase()}`}>
                {formatJobStatus(job.status)}
              </span>
            </div>
            <div className="job-card-body">
              {job.customerName && (
                <span className="meta-item">
                  <Building2 size={14} />
                  <span>{job.customerName}</span>
                </span>
              )}
              {job.customerEmail && (
                <span className="meta-item">
                  <Mail size={14} />
                  <span>{job.customerEmail}</span>
                </span>
              )}
              {job.customerAddress && (
                <span className="meta-item">
                  <MapPin size={14} />
                  <span>{job.customerAddress}</span>
                </span>
              )}
            </div>
            <div className="job-card-meta">
              <span className="meta-item meta-item--muted">
                <Clock size={14} />
                <span>Sidst opdateret: {formatDateLong(job.updatedAt)}</span>
              </span>
            </div>
          </button>
        ))}

        {user.assignedJobs.length === 0 && (
          <div className="empty-state">
            <p>Ingen tildelte opgaver.</p>
          </div>
        )}
      </CollapsibleSection>
    </div>
  );

  function renderAssignableJobCard(job: SearchResult) {
    const isAssigning = assigningJobId === job.id;
    const isDisabled = isAssigning || job.softDeleted;
    const customerLabel = job.customerName ?? job.customer?.name ?? null;
    const alreadyAssigned = job.assignedUsers.some((u) => u.id === id);

    return (
      <div
        key={job.id}
        className={`job-card${isDisabled ? ' job-card--disabled' : ''}`}
        onClick={() => navigate(`/app/job/${job.id}`)}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') navigate(`/app/job/${job.id}`); }}
        role="link"
        tabIndex={0}
      >
        <div className="job-card-top">
          <div>
            <span className="job-number">
              SAG-{(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}<span className="job-number-sep">&middot;</span><span className="job-number-status">{formatJobStatus(job.status)}</span>
            </span>
            <h3 className="job-customer">{customerLabel}</h3>
          </div>
        </div>

        <p className="job-address-row">
          <MapPin size={14} />
          <span className="job-address">{job.destinationAddress || job.customer?.address || 'Ingen adresse angivet'}</span>
        </p>

        <div className="job-card-meta">
          {job.assignedUsers.length > 0 && (
            <span className="meta-item">
              {job.assignedUsers.map((u) => u.displayName).join(', ')}
            </span>
          )}
        </div>

        <div className="job-card-footer">
          {isAssigning ? (
            <span className="meta-item">
              <Loader2 size={14} className="animate-spin" />
              <span>{alreadyAssigned ? 'Fjerner...' : 'Tildeler...'}</span>
            </span>
          ) : job.softDeleted ? (
            <span className="meta-item meta-item--muted">
              <span>Slettet</span>
            </span>
          ) : alreadyAssigned ? (
            <button
              type="button"
              className="btn btn-sm btn-outline-danger"
              onClick={(e) => { e.stopPropagation(); handleAssign(job); }}
            >
              <UserMinus size={14} />
              <span>Fjern</span>
            </button>
          ) : (
            <button
              type="button"
              className="btn btn-sm btn-primary"
              onClick={(e) => { e.stopPropagation(); handleAssign(job); }}
            >
              <UserPlus size={14} />
              <span>Tildel</span>
            </button>
          )}
          <span className="btn-icon" aria-label="Gå til sag">
            <ChevronRight size={20} />
          </span>
        </div>
      </div>
    );
  }
};
