import { useEffect, useMemo, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ErrorState } from '../../../components/ErrorState';
import { useQueryClient } from '@tanstack/react-query';
import { notify } from '../../../lib/toast';
import {
  ArrowLeft,
  Building2,
  Check,
  Clock,
  Loader2,
  Mail,
  MapPin,
  Search,
  Shield,
  Timer,
  UserPlus,
} from 'lucide-react';
import { useGetApiUsersId, getGetApiUsersIdQueryKey } from '../../../api/generated/users/users';
import { useGetApiJobs, usePostApiJobsIdAssign } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models';
import { formatDateLong } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';
import { announceSection } from '../../../components/filters/StatusFilter';


type SearchResult = {
  id: string;
  reportNumber: string | null;
  status: string;
  softDeleted: boolean;
  customer: {
    name: string | null;
  } | null;
  customerName?: string | null;
  customerEmail?: string | null;
  customerAddress?: string | null;
  assignedUsers: { id: string }[];
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
      ? { reportNumber: debouncedSearch, status: [...ASSIGNABLE_STATUSES], limit: SEARCH_LIMIT }
      : undefined,
    {
      query: {
        enabled: debouncedSearch.length >= 2,
      },
    },
  );

  const customerSearchQuery = useGetApiJobs(
    debouncedSearch.length >= 2
      ? { customerName: debouncedSearch, status: [...ASSIGNABLE_STATUSES], limit: SEARCH_LIMIT }
      : undefined,
    {
      query: {
        enabled: debouncedSearch.length >= 2,
      },
    },
  );

  const searchResults: SearchResult[] = useMemo(() => {
    const a = (searchQuery.data) as unknown;
    const b = (customerSearchQuery.data) as unknown;
    const arrA = Array.isArray(a) ? (a as SearchResult[]) : [];
    const arrB = Array.isArray(b) ? (b as SearchResult[]) : [];
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
    { status: [...ASSIGNABLE_STATUSES], limit: SUGGESTION_LIMIT },
    {
      query: {
        enabled: !isSearching,
      },
    },
  );

  const suggestionResults: SearchResult[] = useMemo(() => {
    const raw = (suggestionsQuery.data) as unknown;
    return Array.isArray(raw) ? (raw as SearchResult[]) : [];
  }, [suggestionsQuery.data]);

  const assignMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: getGetApiUsersIdQueryKey(id!) });
        notify.success('Brugeren er tilknyttet sagen');
        setAssigningJobId(null);
        setSearchValue('');
        setDebouncedSearch('');
      },
      onError: () => {
        notify.error('Kunne ikke tilknytte bruger til sagen');
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
    if (currentUserIds.includes(id!)) {
      notify.info('Brugeren er allerede tilknyttet denne sag');
      return;
    }

    setAssigningJobId(job.id);
    assignMutation.mutate({
      id: job.id,
      data: { userIds: [...currentUserIds, id!] },
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
        {user.totalHours != null && (
          <div className="detail-row">
            <Timer size={16} />
            <span>{user.totalHours} timer i alt</span>
          </div>
        )}
      </div>

      <h3 className="section-title">Tildelte opgaver</h3>

      <div className="job-list">
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
      </div>

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
    </div>
  );

  function renderAssignableJobCard(job: SearchResult) {
    const alreadyAssigned = job.assignedUsers.some((u) => u.id === id);
    const isAssigning = assigningJobId === job.id;
    const isDisabled = isAssigning || job.softDeleted;
    const customerLabel = job.customerName ?? job.customer?.name ?? null;

    return (
      <button
        key={job.id}
        className={`job-card${isDisabled ? ' job-card--disabled' : ''}`}
        onClick={() => handleAssign(job)}
        disabled={isDisabled}
        type="button"
      >
        <div className="job-card-top">
          <div>
            <span className="job-number">
              {formatJobNumber(job.reportNumber, job.id)}
            </span>
          </div>
          <span className={`status-badge status-${job.status.toLowerCase()}`}>
            {formatJobStatus(job.status)}
          </span>
        </div>
        <div className="job-card-body">
          {customerLabel && (
            <span className="meta-item">
              <Building2 size={14} />
              <span>{customerLabel}</span>
            </span>
          )}
          {job.customerAddress && (
            <span className="meta-item">
              <MapPin size={14} />
              <span>{job.customerAddress}</span>
            </span>
          )}
        </div>
        <div className="job-card-footer">
          {alreadyAssigned ? (
            <span className="meta-item meta-item--success">
              <Check size={14} />
              <span>Allerede tildelt</span>
            </span>
          ) : isAssigning ? (
            <span className="meta-item">
              <Loader2 size={14} className="animate-spin" />
              <span>Tildeler...</span>
            </span>
          ) : job.softDeleted ? (
            <span className="meta-item meta-item--muted">
              <span>Slettet</span>
            </span>
          ) : (
            <span className="btn btn-sm btn-primary">
              <UserPlus size={14} />
              <span>Tildel</span>
            </span>
          )}
        </div>
      </button>
    );
  }
};
