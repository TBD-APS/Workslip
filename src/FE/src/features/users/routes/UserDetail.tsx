import { useEffect, useMemo, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  AlertCircle,
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
} from 'lucide-react';
import { useGetApiUsersId, getGetApiUsersIdQueryKey } from '../../../api/generated/users/users';
import { useGetApiJobs, usePostApiJobsIdAssign } from '../../../api/generated/jobs/jobs';
import { formatDate } from '../../../lib/formatDate';
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
  assignedUsers: { id: string }[];
};

function formatJobNumber(reportNumber: string | null, id: string) {
  const prefix = reportNumber || id.slice(0, 4);
  return `SAG-${prefix.toUpperCase()}`;
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

  const searchQuery = useGetApiJobs(
    debouncedSearch.length >= 2
      ? { reportNumber: debouncedSearch, limit: 30 }
      : undefined,
    {
      query: {
        enabled: debouncedSearch.length >= 2,
      },
    },
  );

  const searchResults: SearchResult[] = useMemo(() => {
    const responseData = (searchQuery.data);
    return Array.isArray(responseData) ? responseData as SearchResult[] : [];
  }, [searchQuery.data]);

  const assignMutation = usePostApiJobsIdAssign({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: getGetApiUsersIdQueryKey(id!) });
        toast.success('Brugeren er tilknyttet sagen');
        setAssigningJobId(null);
        setSearchValue('');
        setDebouncedSearch('');
      },
      onError: () => {
        toast.error('Kunne ikke tilknytte bruger til sagen');
        setAssigningJobId(null);
      },
    },
  });

  const handleAssign = (job: SearchResult) => {
    if (assigningJobId) return;
    if (job.softDeleted) {
      toast.error('Sagen er slettet og kan ikke tildeles');
      return;
    }

    const currentUserIds = job.assignedUsers.map((u) => u.id);
    if (currentUserIds.includes(id!)) {
      toast.info('Brugeren er allerede tilknyttet denne sag');
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
        <div className="page-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
      </div>
    );
  }

  if (query.isError || !user) {
    return (
      <div className="page-container">
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente brugeroplysninger.</p>
          <button className="btn btn-primary" onClick={() => navigate('/app/users')}>
            Tilbage til brugerliste
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="page-container">
      <div className="page-header">
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
            onClick={() => navigate(`/app/completed/${job.reportId}`)}
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
            <div className="job-card-footer">
              <span className="meta-item">
                <Clock size={14} />
                <span>Sidst opdateret: {formatDate(job.updatedAt)}</span>
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
          placeholder="Søg på sagsnummer..."
          value={searchValue}
          onChange={(e) => setSearchValue(e.target.value)}
        />
      </div>

      {debouncedSearch.length >= 2 && (
        <div className="job-list">
          {searchQuery.isLoading && (
            <div className="empty-state">
              <p>Søger...</p>
            </div>
          )}

          {searchQuery.isError && (
            <div className="error-state">
              <AlertCircle size={32} />
              <p>Kunne ikke søge efter sager.</p>
            </div>
          )}

          {!searchQuery.isLoading && !searchQuery.isError && searchResults.length === 0 && (
            <div className="empty-state">
              <p>Ingen sager fundet.</p>
            </div>
          )}

          {searchResults.map((job) => {
            const alreadyAssigned = job.assignedUsers.some((u) => u.id === id);
            const isAssigning = assigningJobId === job.id;
            const isDisabled = isAssigning || job.softDeleted;

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
                  {job.customer?.name && (
                    <span className="meta-item">
                      <Building2 size={14} />
                      <span>{job.customer.name}</span>
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
                    <span className="meta-item meta-item--action">
                      <span>Tildel</span>
                    </span>
                  )}
                </div>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
};
