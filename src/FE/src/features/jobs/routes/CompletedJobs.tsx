import { useMemo, useEffect, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { AlertCircle, ChevronRight, FileCheck2, MapPin, Timer, User } from 'lucide-react';
import { useGetApiJobs } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import type { AssignedUserResponse, JobListItemViewModel } from '../../../api/generated/models';
import { useAuth } from '../../../providers/useAuth';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';
import { formatJobStatus } from '../statusLabels';
import { StatusFilter, getSavedStatusFilter, saveStatusFilter, announceSection } from '../../../components/filters/StatusFilter';

const CompletedJobSkeletonCard = () => (
  <div className="job-card job-card-skeleton" aria-hidden="true">
    <div className="job-card-header">
      <div className="skeleton skeleton-badge" />
      <div className="skeleton skeleton-id" />
    </div>
    <div className="skeleton skeleton-name" />
    <div className="skeleton skeleton-address" />
    <div className="job-card-footer">
      <div className="skeleton skeleton-tag" />
      <div className="skeleton skeleton-badge" />
    </div>
  </div>
);

export const CompletedJobs = () => {
  const navigate = useNavigate();
  const { user } = useAuth();
  const isAdmin = useIsAdmin();
  const [selectedStatuses, setSelectedStatuses] = useState<JobStatus[]>(() =>
    getSavedStatusFilter('completed', [JobStatus.InReview, JobStatus.Approved]),
  );
  const query = useGetApiJobs({ status: [JobStatus.InReview, JobStatus.Approved, JobStatus.Rejected], limit: 200 });
  const allJobs = query.data ?? [];

  const jobs = useMemo(() => {
    let result = allJobs;

    if (!isAdmin) {
      const currentUserId = user?.id;
      if (!currentUserId) return [];
      result = result.filter((job) => job.assignedUsers.some((assignedUser) => assignedUser.id === currentUserId));
    }

    result = result.filter((job) => selectedStatuses.includes(job.status));

    return result;
  }, [allJobs, isAdmin, user?.id, selectedStatuses]);

  useEffect(() => {
    saveStatusFilter('completed', selectedStatuses);
  }, [selectedStatuses]);

  useEffect(() => {
    announceSection('completed');
  }, []);

  useEffect(() => {
    const handler = (e: PageTransitionEvent) => {
      if (e.persisted) {
        setSelectedStatuses(getSavedStatusFilter('completed', [JobStatus.InReview, JobStatus.Approved]));
      }
    };
    window.addEventListener('pageshow', handler);
    return () => window.removeEventListener('pageshow', handler);
  }, []);

  useEffect(() => {
    document.querySelector<HTMLElement>('.app-content')?.scrollTo(0, 0);
  }, []);

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="page-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
        <div className="job-list">
          <CompletedJobSkeletonCard />
          <CompletedJobSkeletonCard />
        </div>
      </div>
    );
  }

  if (query.isError) {
    return (
      <div className="page-container">
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente afsluttede sager.</p>
          <button className="btn btn-primary" onClick={() => query.refetch()}>
            Prøv igen
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="page-container">
      <div className="page-header">
        <h2>Afsluttede sager</h2>
        {isAdmin ? (
          <p className="subtitle">{jobs.length} indsendte sager klar til PDF</p>
        ) : (
          <p className="subtitle">Viser kun afsluttede sager tildelt dig · {jobs.length} {jobs.length === 1 ? 'sag' : 'sager'}</p>
        )}
      </div>

      <StatusFilter
        options={[
          { value: JobStatus.InReview, label: 'Til gennemsyn' },
          { value: JobStatus.Approved, label: 'Godkendt' },
          { value: JobStatus.Rejected, label: 'Afvist' },
        ]}
        selected={selectedStatuses}
        onChange={setSelectedStatuses}
      />

      <div className="job-list">
        {jobs.map((job) => (
          <CompletedJobCard
            key={job.id}
            job={job}
            onOpen={() => navigate(`/app/completed/${job.id}`)}
          />
        ))}

        {jobs.length === 0 && (
          <div className="empty-state">
            <FileCheck2 size={32} />
            <p>{isAdmin ? 'Der er ingen afsluttede sager endnu.' : 'Du har ingen afsluttede sager tildelt endnu.'}</p>
          </div>
        )}
      </div>
    </div>
  );
};

function CompletedJobCard({
  job,
  onOpen,
}: {
  job: JobListItemViewModel;
  onOpen: () => void;
}) {
  const handleKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onOpen();
    }
  };

  return (
    <article className="job-card completed-job-card" role="button" tabIndex={0} onClick={onOpen} onKeyDown={handleKeyDown}>
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
            <Timer size={14} /> {job.totalHours} t
          </span>
        )}
      </div>

      <div className="job-card-footer completed-job-card-footer">
        <AssignedUsers users={job.assignedUsers} />
        <span className="btn-icon" aria-label="Åbn sagsoverblik">
          <ChevronRight size={20} />
        </span>
      </div>
    </article>
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
