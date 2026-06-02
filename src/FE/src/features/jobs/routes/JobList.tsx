/*import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { MapPin, ChevronRight, AlertCircle, User, Timer, CalendarDays, Flame } from 'lucide-react';
import { getJobs } from '../api/getJobs';

function isDueSoon(reportDate?: string): 'overdue' | 'today' | 'upcoming' | null {
  if (!reportDate) return null;
  const date = new Date(reportDate);
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const diff = Math.floor((date.getTime() - today.getTime()) / 86400000);
  if (diff < 0) return 'overdue';
  if (diff === 0) return 'today';
  if (diff <= 3) return 'upcoming';
  return null;
}

function formatRelativeDate(reportDate?: string): string {
  if (!reportDate) return 'Ingen dato';
  const date = new Date(reportDate);
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const diff = Math.floor((date.getTime() - today.getTime()) / 86400000);
  if (diff < 0) return `Oprettet ${Math.abs(diff) === 1 ? 'i går' : `for ${Math.abs(diff)} dage siden`}`;
  if (diff === 0) return 'I dag';
  if (diff === 1) return 'I morgen';
  return date.toLocaleDateString('da-DK', { weekday: 'long', day: 'numeric', month: 'short' });
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
  const { data: jobs, isLoading, error } = useQuery<JobListItemViewModel[]>({
    queryKey: ['jobs'],
    queryFn: getJobs,
  });

  if (isLoading) {
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

  if (error) {
    return (
      <div className="page-container">
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente jobs. Sørg for at du er logget ind.</p>
          <button className="btn btn-primary" onClick={() => window.location.reload()}>
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
        <p className="subtitle">{jobs?.length || 0} registrerede opgaver</p>
      </div>

      <div className="job-list">
        {jobs?.map((job) => {
          const urgency = isDueSoon(job.reportDate);
          return (
            <div
              key={job.id}
              className={`job-card${urgency ? ' is-urgent' : ''}`}
              onClick={() => navigate(`/app/job/${job.id}`)}
            >
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
                <span className="job-address">
                  {job.customer?.address || 'Ingen adresse angivet'}
                </span>
              </p>

              <div className="job-card-meta">
                <span className="meta-item">
                  <CalendarDays size={14} />
                  {formatRelativeDate(job.reportDate)}
                </span>

                {urgency === 'overdue' && (
                  <span className="meta-badge is-urgent">
                    <Flame size={12} /> Overskredet
                  </span>
                )}
                {urgency === 'today' && (
                  <span className="meta-badge is-today">I dag</span>
                )}
                {urgency === 'upcoming' && (
                  <span className="meta-badge is-upcoming">Snart</span>
                )}

                {job.totalHours != null && (
                  <span className="meta-item meta-hours">
                    <Timer size={14} /> {job.totalHours} t
                  </span>
                )}
              </div>

              <div className="job-card-footer">
                <div className="job-assigned">
                  {job.assignedUsers?.length > 0 ? (
                    job.assignedUsers.slice(0, 2).map((u) => (
                      <span key={u.userId} className="assigned-user">
                        <User size={12} />
                        <span>{u.displayName}</span>
                      </span>
                    ))
                  ) : (
                    <span className="unassigned">
                      <User size={12} />
                      <span>Ikke tildelt</span>
                    </span>
                  )}
                  {job.assignedUsers && job.assignedUsers.length > 2 && (
                    <span className="assigned-user">+{job.assignedUsers.length - 2}</span>
                  )}
                </div>
                <button className="btn-icon" aria-label="Åbn sag">
                  <ChevronRight size={20} />
                </button>
              </div>
            </div>
          );
        })}

        {jobs?.length === 0 && (
          <div className="empty-state">
            <p>Du har ingen opgaver endnu.</p>
          </div>
        )}
      </div>
    </div>
  );
};
*/