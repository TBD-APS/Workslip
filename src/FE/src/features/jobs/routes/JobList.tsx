import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { MapPin, Clock, ChevronRight, AlertCircle, User, Timer } from 'lucide-react';
import { getJobs } from '../api/getJobs';
import type { JobListItemViewModel } from '../types';

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
          return (
            <div key={job.id} className="job-card leftborder" onClick={() => navigate(`/app/job/${job.id}`)}>
              <div className="job-card-header">
                <span className={`status-badge status-${job.status.toString().toLowerCase()}`}>
                  {job.status} {/* Top left status on job card */}
                </span>
                <span className="job-id">Sagsnummer: {job.reportNumber || 'Mangler ID'}</span>
              </div>

              <h3 className="job-customer">{job.customer?.name || 'Ukendt kunde'}</h3>

              <div className="job-details">
                <div className="job-detail-item">
                  <MapPin size={14} />
                  <span className="job-address">
                    {job.customer?.address || 'Ingen adresse angivet'}
                  </span>
                </div>
                <div className="job-detail-row">
                  


                  
                  {job.totalHours != null && (
                    <div className="job-detail-item">
                      <Timer size={14} />
                      <span>{job.totalHours} timer</span>
                    </div>
                  )}

                  <div className="job-assigned">
                  {job.assignedUsers?.length > 0 ? (
                    job.assignedUsers.map((u, i) => (
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
                </div>

                </div>
              </div>

              <div className="job-card-footer">
                <div className="job-tags">
                {job.workKind && <span className="job-tag">{job.workKind}</span>}
                {job.installationTypes?.map((t, i) => (
                  <span key={i} className="job-tag">{t}</span>
                ))}
              </div>
                <button className="btn-icon">
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
