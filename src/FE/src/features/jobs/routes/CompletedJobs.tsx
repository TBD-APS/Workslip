import { useMemo } from 'react';
import type { KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { AlertCircle, ChevronRight, FileCheck2, MapPin, Timer, User } from 'lucide-react';
import { useGetApiJobs } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import type { AssignedUserResponse, CustomerInfo } from '../../../api/generated/models';
import { useAuth } from '../../../providers/useAuth';
import { useIsAdmin } from '../../../providers/permissions';
import { getResponseData } from '../utils';

type CompletedJobListItemViewModel = {
  id: string;
  organizationId: string;
  customer: CustomerInfo | null;
  reportNumber: string | null;
  status: JobStatus;
  reportDate: string | null;
  submittedAt: string | null;
  installationTypes: string[];
  assignedUsers: AssignedUserResponse[];
  totalHours: number | null;
};

const DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'long', year: 'numeric' });

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
  const query = useGetApiJobs({ status: JobStatus.Submitted, limit: 200 });
  const allJobs = getCompletedJobListItems(query.data);
  const jobs = useMemo(() => {
    const submittedJobs = allJobs.filter((job) => job.status === JobStatus.Submitted);
    if (isAdmin) return submittedJobs;

    const currentUserId = user?.id;
    if (!currentUserId) return [];

    return submittedJobs.filter((job) => job.assignedUsers.some((assignedUser) => assignedUser.id === currentUserId));
  }, [allJobs, isAdmin, user?.id]);

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
  job: CompletedJobListItemViewModel;
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
          Indsendt
        </span>
      </div>

      <p className="job-address-row">
        <MapPin size={14} />
        <span className="job-address">{job.customer?.address || 'Ingen adresse angivet'}</span>
      </p>

      <div className="job-card-meta">
        <span className="meta-item">{formatInstallationTypes(job.installationTypes)}</span>
        {job.submittedAt && <span className="meta-item">Indsendt {formatDate(job.submittedAt)}</span>}
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

function getCompletedJobListItems(value: unknown): CompletedJobListItemViewModel[] {
  const data = getResponseData(value);
  return Array.isArray(data) ? data as CompletedJobListItemViewModel[] : [];
}

function formatInstallationTypes(installationTypes: string[]) {
  if (installationTypes.length === 0) return 'Ingen installationstype';
  if (installationTypes.length === 1) return installationTypes[0];
  return `${installationTypes[0]} +${installationTypes.length - 1}`;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return DATE_FORMATTER.format(date);
}
