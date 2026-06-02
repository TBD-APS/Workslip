import { useNavigate } from 'react-router-dom';
import { AlertCircle, ChevronRight, MapPin, Timer, User } from 'lucide-react';
import { useGetApiJobs } from '../../../api/generated/jobs/jobs';
import type { AssignedUserResponse, CustomerInfo, JobStatus } from '../../../api/generated/models';

type JobListItemViewModel = {
  id: string;
  organizationId: string;
  customer: CustomerInfo | null;
  reportNumber: string | null;
  status: JobStatus;
  installationTypes: string[];
  assignedUsers: AssignedUserResponse[];
  softDeleted: boolean;
  totalHours: number | null;
};

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
  const query = useGetApiJobs({ limit: 200 });
  const jobs = getJobListItems(query.data);

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
        <h2>Opgaver</h2>
        <p className="subtitle">{jobs.length} registrerede opgaver</p>
      </div>

      <div className="job-list">
        {jobs.map((job) => (
          <JobCard key={job.id} job={job} onOpen={() => navigate(`/app/job/${job.id}`)} />
        ))}

        {jobs.length === 0 && (
          <div className="empty-state">
            <p>Du har ingen opgaver endnu.</p>
          </div>
        )}
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
          {job.status}
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

function getJobListItems(value: unknown): JobListItemViewModel[] {
  const data = getResponseData(value);
  return Array.isArray(data) ? data as JobListItemViewModel[] : [];
}

function getResponseData(value: unknown): unknown {
  if (!value || typeof value !== 'object' || !('data' in value)) return value;

  const data = (value as { data: unknown }).data;
  if (data && typeof data === 'object' && 'data' in data) {
    return (data as { data: unknown }).data;
  }

  return data;
}

function formatInstallationTypes(installationTypes: string[]) {
  if (installationTypes.length === 0) return 'Ingen installationstype';
  if (installationTypes.length === 1) return installationTypes[0];
  return `${installationTypes[0]} +${installationTypes.length - 1}`;
}
